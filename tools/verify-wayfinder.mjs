import { execFile } from 'node:child_process'
import { createHash } from 'node:crypto'
import { readFile } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { promisify } from 'node:util'
import { fromMarkdown } from 'mdast-util-from-markdown'

const execute = promisify(execFile)
const normalize = (body) => body.replaceAll('\r\n', '\n')
const hash = (body) => createHash('sha256').update(body).digest('hex')
const sorted = (values) => [...values].sort()
const same = (left, right) => JSON.stringify(sorted(left)) === JSON.stringify(sorted(right))
const positiveInteger = (value) => Number.isSafeInteger(value) && value > 0

export function validateExpectation(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    throw new Error('Expectation must be a JSON object')
  }
  if (typeof value.repo !== 'string' || !/^[\w.-]+\/[\w.-]+$/.test(value.repo)) {
    throw new Error('repo must be owner/repository')
  }
  for (const field of ['map', 'ticket', 'comment']) {
    if (!positiveInteger(value[field])) throw new Error(`${field} must be a positive integer`)
  }
  if (typeof value.bodyFile !== 'string' || !value.bodyFile.trim()) {
    throw new Error('bodyFile must name the expected resolution text file')
  }
  for (const field of ['blockedBy', 'blocking']) {
    if (!Array.isArray(value[field]) || !value[field].every(positiveInteger)
      || new Set(value[field]).size !== value[field].length) {
      throw new Error(`${field} must be an explicit array of unique issue numbers (use [] for none)`)
    }
  }
  return value
}

function bodyDifference(expected, actual) {
  const left = expected.split('\n')
  const right = actual.split('\n')
  const index = Array.from({ length: Math.max(left.length, right.length) }, (_, i) => i)
    .find((i) => left[i] !== right[i])
  return {
    expectedSha256: hash(expected),
    actualSha256: hash(actual),
    firstDifferentLine: index + 1,
    expectedLine: (left[index] ?? '<EOF>').slice(0, 240),
    actualLine: (right[index] ?? '<EOF>').slice(0, 240),
  }
}

function hasResolutionLink(markdown, url) {
  const root = fromMarkdown(markdown)
  const text = (node) => node.type === 'text' || node.type === 'inlineCode'
    ? node.value : (node.children ?? []).map(text).join('')
  const definitions = new Map()
  function collectDefinitions(node) {
    if (node.type === 'definition' && !definitions.has(node.identifier)) {
      definitions.set(node.identifier, node.url)
    }
    for (const child of node.children ?? []) collectDefinitions(child)
  }
  collectDefinitions(root)
  function containsLink(node) {
    const target = node.type === 'link' ? node.url
      : node.type === 'linkReference' ? definitions.get(node.identifier) : undefined
    const label = text(node).trim()
    if (target === url && label && label !== url) return true
    return (node.children ?? []).some(containsLink)
  }
  let inDecisions = false
  for (const node of root.children) {
    if (node.type === 'heading' && node.depth <= 2) {
      inDecisions = node.depth === 2 && text(node).trim() === 'Decisions so far'
    } else if (inDecisions && containsLink(node)) {
      return true
    }
  }
  return false
}

export function verifySnapshot(expected, body, snapshot) {
  validateExpectation(expected)
  const { map, ticket, comment, children, blockedBy, blocking } = snapshot
  const issueUrl = `https://github.com/${expected.repo}/issues/${expected.ticket}`
  const commentUrl = `${issueUrl}#issuecomment-${expected.comment}`
  const identityMatches = map.number === expected.map && ticket.number === expected.ticket
    && ticket.html_url === issueUrl && map.html_url === `https://github.com/${expected.repo}/issues/${expected.map}`
  const commentMatches = comment.id === expected.comment && comment.html_url === commentUrl
    && comment.issue_url === `https://api.github.com/repos/${expected.repo}/issues/${expected.ticket}`
  if (typeof comment.body !== 'string' || typeof map.body !== 'string') {
    throw new Error('GitHub returned an invalid comment or map body')
  }
  const expectedBody = normalize(body)
  const actualBody = normalize(comment.body)
  const indexed = hasResolutionLink(map.body, commentUrl)
  const expectedLinks = (numbers) => sorted(numbers.map((number) => `https://github.com/${expected.repo}/issues/${number}`))
  const actualLinks = (issues) => sorted(issues.map((issue) => issue.html_url ?? `<missing URL: ${issue.number}>`))
  const checks = [
    { name: 'issue-identity', ok: identityMatches },
    { name: 'resolution-belongs-to-ticket', ok: commentMatches },
    { name: 'resolution-body', ok: expectedBody === actualBody,
      ...(expectedBody !== actualBody ? { difference: bodyDifference(expectedBody, actualBody) } : {}) },
    { name: 'ticket-closed-completed', ok: ticket.state === 'closed' && ticket.state_reason === 'completed',
      actual: `${ticket.state}/${ticket.state_reason}` },
    { name: 'map-resolution-link', ok: indexed },
    { name: 'native-sub-issue', ok: children.some((child) => child.number === expected.ticket && child.html_url === issueUrl) },
    { name: 'blocked-by', ok: same(expectedLinks(expected.blockedBy), actualLinks(blockedBy)),
      expected: expectedLinks(expected.blockedBy), actual: actualLinks(blockedBy) },
    { name: 'blocking', ok: same(expectedLinks(expected.blocking), actualLinks(blocking)),
      expected: expectedLinks(expected.blocking), actual: actualLinks(blocking) },
  ]
  return { ok: checks.every((check) => check.ok), ticket: { title: ticket.title, url: issueUrl }, checks }
}

export async function githubRead(endpoint, paginate = false) {
  const args = ['api', '--method', 'GET', endpoint, ...(paginate ? ['--paginate', '--slurp'] : [])]
  const { stdout } = await execute('gh', args, {
    encoding: 'utf8', timeout: 20_000, maxBuffer: 8 * 1024 * 1024, windowsHide: true,
    env: { ...process.env, GH_PROMPT_DISABLED: '1' },
  })
  const value = JSON.parse(stdout)
  if (paginate && (!Array.isArray(value) || !value.every(Array.isArray))) {
    throw new Error(`Expected paginated arrays from ${endpoint}`)
  }
  return paginate ? value.flat() : value
}

export async function collectSnapshot(expected, request = githubRead) {
  validateExpectation(expected)
  const base = `repos/${expected.repo}/issues`
  const [map, ticket, comment, children, blockedBy, blocking] = await Promise.all([
    request(`${base}/${expected.map}`),
    request(`${base}/${expected.ticket}`),
    request(`${base}/comments/${expected.comment}`),
    request(`${base}/${expected.map}/sub_issues`, true),
    request(`${base}/${expected.ticket}/dependencies/blocked_by`, true),
    request(`${base}/${expected.ticket}/dependencies/blocking`, true),
  ])
  return { map, ticket, comment, children, blockedBy, blocking }
}

export async function main(args) {
  if (args.length !== 2 || args[0] !== '--expect') {
    throw new Error('Usage: node tools/verify-wayfinder.mjs --expect path/to/expectation.json')
  }
  const manifestPath = resolve(args[1])
  const expected = validateExpectation(JSON.parse(await readFile(manifestPath, 'utf8')))
  // Read the submitted text independently of GitHub: fetching it as the baseline would be tautological.
  const body = await readFile(resolve(dirname(manifestPath), expected.bodyFile), 'utf8')
  return verifySnapshot(expected, body, await collectSnapshot(expected))
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const result = await main(process.argv.slice(2))
    console.log(JSON.stringify(result, null, 2))
    process.exitCode = result.ok ? 0 : 1
  } catch (error) {
    console.error(JSON.stringify({ ok: false, error: String(error.message).slice(0, 600) }))
    process.exitCode = 2
  }
}
