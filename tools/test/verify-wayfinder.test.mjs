import assert from 'node:assert/strict'
import { test } from 'node:test'
import { collectSnapshot, validateExpectation, verifySnapshot } from '../verify-wayfinder.mjs'

const expected = {
  repo: 'example/project', map: 8, ticket: 10, comment: 123,
  bodyFile: 'resolution.md', blockedBy: [9], blocking: [11, 12],
}
const body = '## Resolution\n\n用户确认的结论。\n'
const issueUrl = 'https://github.com/example/project/issues/10'
const commentUrl = `${issueUrl}#issuecomment-123`
function snapshot() {
  return {
    map: { number: 8, html_url: 'https://github.com/example/project/issues/8',
      body: `## Destination\n\n规划。\n\n## Decisions so far\n\n- [生命周期](${commentUrl})：摘要。\n\n## Not yet specified\n\n迷雾。\n` },
    ticket: { number: 10, title: '生命周期', html_url: issueUrl, state: 'closed', state_reason: 'completed' },
    comment: { id: 123, body, html_url: commentUrl, issue_url: 'https://api.github.com/repos/example/project/issues/10' },
    children: [{ number: 10, html_url: issueUrl }],
    blockedBy: [{ number: 9, html_url: 'https://github.com/example/project/issues/9' }],
    blocking: [
      { number: 12, html_url: 'https://github.com/example/project/issues/12' },
      { number: 11, html_url: 'https://github.com/example/project/issues/11' },
    ],
  }
}
const check = (result, name) => result.checks.find((entry) => entry.name === name)

test('matching readback is compact and does not print the resolution or map', () => {
  const result = verifySnapshot(expected, body, snapshot())
  assert.equal(result.ok, true)
  assert.equal(result.checks.length, 8)
  assert.equal(JSON.stringify(result).includes('用户确认的结论'), false)
  assert.equal(JSON.stringify(result).includes('Not yet specified'), false)
})

test('CRLF and LF compare equally, meaningful whitespace does not', () => {
  assert.equal(verifySnapshot(expected, body.replaceAll('\n', '\r\n'), snapshot()).ok, true)
  assert.equal(check(verifySnapshot(expected, body + '\n', snapshot()), 'resolution-body').ok, false)
})

test('a changed body reports the first bounded difference, not the entire text', () => {
  const actual = snapshot()
  actual.comment.body = `## Resolution\n\n${'x'.repeat(2000)}\n`
  const result = verifySnapshot(expected, body, actual)
  const difference = check(result, 'resolution-body').difference
  assert.equal(result.ok, false)
  assert.equal(difference.firstDifferentLine, 3)
  assert.equal(difference.actualLine.length, 240)
  assert.notEqual(difference.expectedSha256, difference.actualSha256)
  assert.equal(JSON.stringify(result).includes('x'.repeat(241)), false)
})

for (const [name, mutate, failure] of [
  ['wrong issue', (s) => { s.ticket.number = 999 }, 'issue-identity'],
  ['comment belongs to another issue', (s) => { s.comment.issue_url += '0' }, 'resolution-belongs-to-ticket'],
  ['wrong comment', (s) => { s.comment.id = 999 }, 'resolution-belongs-to-ticket'],
  ['still open', (s) => { s.ticket.state = 'open' }, 'ticket-closed-completed'],
  ['not planned closure', (s) => { s.ticket.state_reason = 'not_planned' }, 'ticket-closed-completed'],
  ['missing native parent', (s) => { s.children = [] }, 'native-sub-issue'],
  ['missing blocker', (s) => { s.blockedBy = [] }, 'blocked-by'],
  ['unexpected blocking edge', (s) => { s.blocking.push({ number: 99 }) }, 'blocking'],
  ['link only in fog', (s) => { s.map.body = `## Decisions so far\n\n## Not yet specified\n[生命周期](${commentUrl})\n` }, 'map-resolution-link'],
  ['bare URL is not an index entry', (s) => { s.map.body = `## Decisions so far\n${commentUrl}\n` }, 'map-resolution-link'],
]) {
  test(`rejects ${name}`, () => {
    const actual = snapshot()
    mutate(actual)
    const result = verifySnapshot(expected, body, actual)
    assert.equal(result.ok, false)
    assert.equal(check(result, failure).ok, false)
  })
}

test('same issue number in another repository is not the expected dependency', () => {
  for (const field of ['blockedBy', 'blocking']) {
    const actual = snapshot()
    actual[field][0].html_url = actual[field][0].html_url.replace('/project/', '/other/')
    const result = verifySnapshot(expected, body, actual)
    assert.equal(result.ok, false)
    assert.equal(check(result, field === 'blockedBy' ? 'blocked-by' : 'blocking').ok, false)
  }
})

test('only rendered named links count as the map index', () => {
  for (const hidden of [
    `<!-- [生命周期](${commentUrl}) -->`,
    `\`\`\`md\n[生命周期](${commentUrl})\n\`\`\``,
    `    [生命周期](${commentUrl})`,
    `\`[生命周期](${commentUrl})\``,
    `\\[生命周期](${commentUrl})`,
  ]) {
    const actual = snapshot()
    actual.map.body = `## Decisions so far\n\n${hidden}\n`
    assert.equal(check(verifySnapshot(expected, body, actual), 'map-resolution-link').ok, false, hidden)
  }
})

test('reference-style named links are valid index entries', () => {
  const actual = snapshot()
  actual.map.body = `## Decisions so far\n\n- [生命周期][resolution]\n\n## Not yet specified\n\n[resolution]: ${commentUrl}\n`
  assert.equal(verifySnapshot(expected, body, actual).ok, true)
})

test('a heading inside a code block cannot start the decisions section', () => {
  const actual = snapshot()
  actual.map.body = `\`\`\`md\n## Decisions so far\n\`\`\`\n\n[生命周期](${commentUrl})\n`
  assert.equal(check(verifySnapshot(expected, body, actual), 'map-resolution-link').ok, false)
})

test('a dependency without its repository identity fails verification', () => {
  const actual = snapshot()
  delete actual.blockedBy[0].html_url
  assert.equal(check(verifySnapshot(expected, body, actual), 'blocked-by').ok, false)
})

test('an index at end of the map is supported', () => {
  const actual = snapshot()
  actual.map.body = `## Decisions so far\n\n- [生命周期](${commentUrl})：摘要。`
  assert.equal(verifySnapshot(expected, body, actual).ok, true)
})

test('expectation must explicitly list both dependency directions', () => {
  assert.throws(() => validateExpectation({ ...expected, blockedBy: undefined }), /blockedBy/)
  assert.throws(() => validateExpectation({ ...expected, blocking: [11, 11] }), /blocking/)
  assert.throws(() => validateExpectation({ ...expected, repo: '../wrong/repo' }), /repo/)
  assert.throws(() => validateExpectation({ ...expected, ticket: 0 }), /ticket/)
  assert.throws(() => validateExpectation({ ...expected, bodyFile: '' }), /bodyFile/)
  assert.doesNotThrow(() => validateExpectation({ ...expected, blockedBy: [], blocking: [] }))
})

test('collects only six read endpoints, and paginates all relationships', async () => {
  const calls = []
  await collectSnapshot(expected, async (endpoint, paginate = false) => {
    calls.push({ endpoint, paginate })
    return paginate ? [] : {}
  })
  assert.deepEqual(calls, [
    { endpoint: 'repos/example/project/issues/8', paginate: false },
    { endpoint: 'repos/example/project/issues/10', paginate: false },
    { endpoint: 'repos/example/project/issues/comments/123', paginate: false },
    { endpoint: 'repos/example/project/issues/8/sub_issues', paginate: true },
    { endpoint: 'repos/example/project/issues/10/dependencies/blocked_by', paginate: true },
    { endpoint: 'repos/example/project/issues/10/dependencies/blocking', paginate: true },
  ])
})

test('transport failure is not treated as an empty list or successful verification', async () => {
  await assert.rejects(collectSnapshot(expected, async () => { throw new Error('HTTP 403') }), /HTTP 403/)
})
