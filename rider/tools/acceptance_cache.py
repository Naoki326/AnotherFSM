"""Small content-addressed preparation phases; no builds, installs or launches on import.

Public API: project_inputs (conservative literal MSBuild closure), stage_plan,
cached_execute. Callers supply *all* external toolchain/configuration inputs and
mark anything they cannot certify as incomplete. An incomplete plan always runs.
Receipts are evidence of this tool's successful execution, never an adoption of
pre-existing output. Hash-only sandbox installation ownership stays separate.
"""
from __future__ import annotations

from contextlib import contextmanager
from dataclasses import dataclass
import glob
import hashlib
import json
import os
from pathlib import Path
import re
import stat
from typing import Callable, Iterable
import uuid
import xml.etree.ElementTree as ET

from build import clean_environment, managed_directory, no_links

# Input trees only. Output inventories NEVER exclude anything.
EXCLUDED = frozenset({'bin', 'obj', 'artifacts', '.git', '.idea', '__pycache__', '.packages'})


def _hash(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':'),
                                     ensure_ascii=True).encode()).hexdigest()


def _path(path):
    # Check before collapsing '..' as well, so links cannot hide in a prefix.
    return no_links(Path(os.path.abspath(no_links(Path(path)))))


def _entry(path):
    # The walk's root and each ancestor have already passed no_links. Check each
    # child itself (including Windows junctions), without O(depth) repeated stats
    # per SDK file. Never follow an unchecked directory.
    info = path.lstat()
    if stat.S_ISLNK(info.st_mode) or getattr(info, 'st_file_attributes', 0) & 0x400:
        raise ValueError(f'Refusing linked/reparse path: {path}')
    return path


def _files(paths, *, inputs=False):
    result = {}
    for root in sorted({_path(p) for p in paths}):
        key = root.as_posix()
        if not root.exists():
            result[key] = None
        elif root.is_file():
            with root.open('rb') as stream:
                result[key] = hashlib.file_digest(stream, 'sha256').hexdigest()
        elif root.is_dir():
            result[key + '/'] = 'directory'
            for parent, directories, files in os.walk(root, followlinks=False):
                directories[:] = sorted(d for d in directories if not inputs or d not in EXCLUDED)
                for name in directories:
                    _entry(Path(parent) / name)
                for name in sorted(files):
                    path = _entry(Path(parent) / name)
                    with path.open('rb') as stream:
                        result[path.as_posix()] = hashlib.file_digest(stream, 'sha256').hexdigest()
        else:
            raise ValueError(f'Not a regular file/directory: {root}')
    return result


@dataclass(frozen=True)
class ProjectInputs:
    """Pass paths/incomplete to stage_plan, and evidence_sha256 in configuration."""
    paths: tuple[Path, ...]
    incomplete: tuple[str, ...]
    evidence_sha256: str


def project_inputs(projects: Iterable[Path], *, properties=None, assets=(), antlr_tool=None) -> ProjectInputs:
    """Follow literal transitive ProjectReference, linked items and Import paths.

    All conditions are over-approximated (both branches); no MSBuild evaluation,
    restore or business code. Dynamic includes/imports, custom targets and unknown
    SDKs force a miss. Optional existing assets manifests provide package closure;
    whole extracted packages/config files are inputs. Package build logic must be
    warning-only or one of the pinned, supported Antlr/NETStandard task definitions;
    Antlr's local tool JAR must also be explicit. Other package logic forces a miss.
    """
    paths, seen, incomplete = set(), set(), set()
    properties = dict(properties or {})
    # Standard SDK imports/tool selection can consume environment properties without
    # appearing in project XML. Hashing an unchanged path string does not certify
    # external file bytes. Keep the actual compiler environment, but refuse reuse
    # for these unverified override families (MSBuild names are case-insensitive).
    for name, value in clean_environment().items():
        key = name.lower()
        if value and (key.startswith(('directorybuild', 'directorypackages', 'custombefore',
                                      'customafter', 'msbuild', 'roslyn', 'csc', 'vbc',
                                      'frameworkpathoverride', 'targetframeworkrootpath'))
                      or key.endswith(('propspath', 'targetspath'))):
            incomplete.add('unverified MSBuild environment override: ' + name)
    libraries, evidence = set(), {}
    # Existing restore evidence is read, never generated here. Hash *whole*
    # extracted package directories, not just the DLL selected for compilation.
    for asset in assets:
        asset = no_links(Path(asset))
        if not asset.is_file():
            incomplete.add('missing restore evidence: ' + str(asset))
            evidence[str(asset)] = None
            continue
        raw = asset.read_bytes()
        evidence[str(_path(asset))] = hashlib.sha256(raw).hexdigest()
        try:
            data = json.loads(raw)
        except (ValueError, UnicodeError):
            incomplete.add('invalid restore evidence: ' + str(asset))
            continue
        paths.update(Path(p) for p in data.get('project', {}).get('restore', {}).get('configFilePaths', []))
        for identity, library in data.get('libraries', {}).items():
            if library.get('type') != 'package':
                continue
            libraries.add(identity.lower())
            candidates = [Path(root) / library['path'] for root in data.get('packageFolders', {})]
            package = next((p for p in candidates if p.is_dir()), None)
            if package is None:
                incomplete.add('missing restored package: ' + identity)
                continue
            paths.add(no_links(package))
            logic = []
            for parent, directories, files in os.walk(package, followlinks=False):
                for name in directories:
                    no_links(Path(parent) / name)
                logic.extend(no_links(Path(parent) / name) for name in files
                             if Path(name).suffix.lower() in ('.props', '.targets'))
            if logic:
                # Only the existing Antlr task is supported. Its executable logic
                # is pinned; its external grammar/tool/JRE are explicit inputs.
                known = {
                    'build/Antlr4BuildTasks.props': '45f7c5987f8128ba8f9dfb635312e3dda427a18e02f90e5cb3ff7fc666d440de',
                    'build/Antlr4BuildTasks.targets': '017ccd12b3db0102e9d874493e0fd666def26f70048ffd3340d72f313093d732',
                    'lib/netstandard2.0/Antlr4BuildTasks.dll': '7ee0d2582cf47f54379576583c1913a0a853d570a9acbd4ac85cae866027cde3',
                }
                supported = identity.lower() == 'antlr4buildtasks/12.14.0' and antlr_tool is not None
                supported = supported and {p.relative_to(package).as_posix() for p in logic} == set(known) - {'lib/netstandard2.0/Antlr4BuildTasks.dll'}
                supported = supported and all((package / name).is_file() and
                    hashlib.sha256(no_links(package / name).read_bytes()).hexdigest() == sha for name, sha in known.items())
                # Framework compatibility warning-only targets do not add
                # compilation inputs or execute arbitrary tasks.
                warning_only = all(all(n.tag.rsplit('}', 1)[-1] in {'Project', 'Target', 'Warning'}
                                       for n in ET.parse(p).getroot().iter()) for p in logic)
                standard = (identity.lower() == 'netstandard.library/2.0.3' and len(logic) == 1 and
                            hashlib.sha256(no_links(logic[0]).read_bytes()).hexdigest() ==
                            '7a23f0ce3bed999c432e9f786855795dfba6533b4fb33bdaf387f32215f2060d')
                if not supported and not warning_only and not standard:
                    incomplete.add('unverified package build logic: ' + identity)
    if antlr_tool is not None:
        paths.add(Path(antlr_tool))
        if not Path(antlr_tool).is_file():
            incomplete.add('missing local Antlr tool')

    def visit(project, project_dir=None):
        project = _path(project)
        if project.suffix.lower() == '.csproj' or project_dir is None:
            project_dir = project.parent
        context = (project, project_dir)
        if context in seen:
            return
        seen.add(context)
        paths.add(project)
        if not project.is_file():
            incomplete.add('missing project/import: ' + str(project))
            return
        if project.suffix.lower() == '.csproj':
            paths.add(project.parent)
            for parent in project.parents:
                for name in ('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props',
                             'NuGet.Config', 'nuget.config', 'global.json'):
                    candidate = parent / name
                    paths.add(candidate)  # absence also matters
                    if candidate.is_file() and candidate.name == 'global.json':
                        incomplete.add('SDK selection override needs evaluation: ' + str(candidate))
                    if candidate.is_file() and candidate.suffix in ('.props', '.targets'):
                        visit(candidate, project_dir)
        try:
            document = ET.parse(project).getroot()
        except ET.ParseError:
            incomplete.add('unparseable project/import: ' + str(project))
            return
        sdk = document.get('Sdk', '')
        if sdk and sdk not in ('Microsoft.NET.Sdk', 'Microsoft.NET.Sdk.Web', 'Microsoft.NET.Sdk.WebAssembly'):
            incomplete.add('unverified SDK: ' + sdk)
        for node in document.iter():
            tag = node.tag.rsplit('}', 1)[-1]
            replacements = {**properties, 'MSBuildThisFileDirectory': project.parent.as_posix() + '/',
                            'MSBuildProjectDirectory': project_dir.as_posix()}
            if any('$([' in value for value in [node.text or '', *node.attrib.values()]):
                incomplete.add('unevaluated property function: ' + str(project))
            condition = node.get('Condition', '')
            for _, dependency in re.findall(r'''Exists\(\s*(['"])(.*?)\1\s*\)''', condition, re.IGNORECASE):
                for key, value in replacements.items():
                    dependency = dependency.replace('$(' + key + ')', str(value))
                if any(token in dependency for token in ('$(', '@(', '%(')):
                    # AntlrJavaExec is supplied globally, so its project-local
                    # fallback conditions cannot affect the actual invocation.
                    if tag != 'AntlrJavaExec' or 'AntlrJavaExec' not in properties:
                        incomplete.add('unresolved file condition: ' + str(project))
                else:
                    paths.add(no_links(project_dir / dependency.replace('\\', '/')))
            if tag == 'Import' and node.get('Sdk'):
                incomplete.add('unverified SDK import: ' + str(project))
            if tag in ('Target', 'UsingTask', 'Sdk'):
                incomplete.add('unverified custom build logic: ' + str(project))
            if tag in ('AntlrToolJar', 'AntlrToolJarDownloadDir', 'JavaDownloadDirectory',
                       'LibPath', 'Antlr4BuildTaskPath', 'Antlr4BuildTaskAssemblyName', 'AntlrNgPath') and (node.text or '').strip():
                incomplete.add('unverified Antlr external input: ' + str(project))
            if tag == 'PackageReference':
                identity = node.get('Include', '') + '/' + node.get('Version', '').strip('[]')
                if identity.lower() not in libraries:
                    incomplete.add('unverified package build dependencies: ' + str(project))
                continue
            text = node.get('Project') if tag == 'Import' else node.get('Include')
            if tag == 'Reference':
                text = node.findtext('HintPath') or (text if text and ('/' in text or '\\' in text) else None)
                if not text:
                    incomplete.add('unresolved assembly reference: ' + str(project))
            if not text:
                continue
            for key, value in replacements.items():
                text = text.replace('$(' + key + ')', str(value))
            if any(token in text for token in ('$(', '@(', '%(')):
                incomplete.add('unresolved ' + tag + ': ' + str(project))
                continue
            for item in text.replace('\\', '/').split(';'):
                # Imported item paths are relative to the consuming project;
                # Import paths themselves are relative to the importing file.
                path = (project.parent if tag == 'Import' else project_dir) / item
                if glob.has_magic(str(path)):
                    # Watch the whole non-wildcard prefix. Do not traverse a
                    # wildcard graph edge we cannot statically certify.
                    prefix = Path(str(path).split('*', 1)[0].split('?', 1)[0].split('[', 1)[0])
                    paths.add(no_links(prefix if prefix.is_dir() else prefix.parent))
                    if tag in ('Import', 'ProjectReference'):
                        incomplete.add('wildcard graph edge: ' + str(project))
                    continue
                for match in [path]:
                    paths.add(no_links(match))
                    if tag == 'Antlr4':
                        paths.add(no_links(match.parent))
                    if tag in ('Import', 'ProjectReference'):
                        visit(match, project_dir if tag == 'Import' else None)
    for project in projects:
        visit(project)
    return ProjectInputs(tuple(sorted({_path(p) for p in paths})), tuple(sorted(incomplete)), _hash(evidence))


@dataclass(frozen=True)
class StagePlan:
    name: str
    input_paths: tuple[Path, ...]
    outputs: tuple[Path, ...]
    configuration: dict
    inputs: dict
    input_sha256: str
    incomplete: tuple[str, ...]


def stage_plan(name: str, *, inputs: Iterable[Path], outputs: Iterable[Path],
               configuration=None, incomplete=()) -> StagePlan:
    """Snapshot content and configuration; case selection is not a compiler input."""
    if not re.fullmatch(r'[a-zA-Z0-9][a-zA-Z0-9_.-]*', name):
        raise ValueError('Invalid stage name')
    paths = tuple(_path(p) for p in inputs)
    output_paths = tuple(_path(p) for p in outputs)
    if not output_paths:
        raise ValueError('A stage must declare outputs')
    # Copy mutable caller configuration, rejecting non-JSON values.
    config = json.loads(json.dumps(configuration or {}, sort_keys=True))
    inventory = _files(paths, inputs=True)
    key = _hash({'schema': 1, 'stage': name, 'files': inventory, 'configuration': config})
    return StagePlan(name, paths, output_paths, config, inventory, key, tuple(incomplete))


def content_fingerprint(paths: Iterable[Path]) -> dict:
    """Hash external SDK trees once per preparation; no time/dirty shortcuts."""
    files = _files(paths)
    return {'sha256': _hash(files), 'fileCount': len(files)}


@contextmanager
def _locked(directory):
    directory = managed_directory(directory)
    lock = no_links(directory / '.cache-lock')
    try:
        stream = lock.open('x', encoding='ascii')
    except FileExistsError:
        raise ValueError(f'Another preparation owns {lock}; inspect before removing') from None
    try:
        with stream:
            stream.write(str(os.getpid()))
        yield directory
    finally:
        lock.unlink()


def cached_execute(cache: Path, plan: StagePlan, execute: Callable[[], object], *, force=False) -> dict:
    """Execute or verify-reuse one phase, returning JSON-ready runner evidence.

    Refuse unowned cache roots/links. Invalidate the old receipt before execution;
    failure or concurrent input edits cannot bless stale output. A cache miss does
    not grant permission to overwrite any installation: that remains the caller's
    hash-only inventory responsibility.
    """
    with _locked(Path(cache)) as directory:
        receipt = no_links(directory / (plan.name + '.json'))
        if _files(plan.input_paths, inputs=True) != plan.inputs:
            raise RuntimeError('Stage inputs changed since planning: ' + plan.name)
        current = _files(plan.outputs)
        reason = 'no trusted receipt'
        previous = None
        if receipt.is_file():
            try:
                previous = json.loads(receipt.read_text(encoding='utf-8'))
            except (ValueError, UnicodeError):
                reason = 'invalid receipt'
        if force:
            reason = 'forced'
        elif plan.incomplete:
            reason = 'incomplete input closure: ' + '; '.join(plan.incomplete)
        elif isinstance(previous, dict) and previous.get('schemaVersion') == 1:
            if previous.get('incomplete'):
                reason = 'previous receipt had incomplete inputs'
            elif previous.get('inputSha256') != plan.input_sha256:
                reason = 'inputs changed'
            elif previous.get('outputs') != current or any(v is None for v in current.values()):
                reason = 'outputs changed or missing'
            elif previous.get('outputSha256') == _hash(current):
                return {**previous, 'status': 'cached', 'reason': 'input and output hashes match', 'receipt': str(receipt)}
            else:
                reason = 'invalid output digest'
        receipt.unlink(missing_ok=True)
        execute()
        if _files(plan.input_paths, inputs=True) != plan.inputs:
            raise RuntimeError('Stage inputs changed during execution: ' + plan.name)
        outputs = _files(plan.outputs)
        if any(value is None for value in outputs.values()):
            raise RuntimeError('Stage did not produce declared outputs: ' + plan.name)
        result = {'schemaVersion': 1, 'stage': plan.name, 'status': 'executed', 'reason': reason,
                  'inputSha256': plan.input_sha256, 'outputSha256': _hash(outputs),
                  'outputs': outputs, 'inputFileCount': len(plan.inputs),
                  'configuration': plan.configuration, 'incomplete': list(plan.incomplete),
                  'receipt': str(receipt)}
        temporary = no_links(directory / (plan.name + '.' + uuid.uuid4().hex + '.tmp'))
        try:
            temporary.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
            temporary.replace(receipt)
        finally:
            temporary.unlink(missing_ok=True)
        return result
