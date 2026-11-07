#!/usr/bin/env python3
"""Prepare only controlled fixtures and a separate read-only host test plugin."""
import argparse
import json
import os
from pathlib import Path
import shutil
import re
import subprocess
import sys
import uuid

sys.dont_write_bytecode = True
from build import (ROOT, RIDER_ROOT, DEFAULT_RIDER, DEFAULT_ZIP, LOGS, host, run,
                   build, clean_environment, managed_directory, zip_bytes, argfile, digest, no_links)
import sandbox
from acceptance_contract import record_preparation, validate_case
from acceptance_cache import cached_execute, content_fingerprint, project_inputs, stage_plan


def inventory(directory):
    result = {}
    for path in directory.rglob('*'):
        no_links(path)
        if path.is_file():
            result[path.relative_to(directory).as_posix()] = digest(path.read_bytes())
    return result


def solution_projects(solution):
    return [no_links(solution.parent / name.replace('\\', '/')) for name in
            re.findall(r'Project\([^\n]+?=\s*"[^"]+",\s*"([^"]+\.csproj)"',
                       solution.read_text(encoding='utf-8-sig'))]


def compiler_environment(home, logs):
    """Local queries only. Hash installed compilers/SDKs, not version strings alone."""
    dotnet = Path(shutil.which('dotnet') or 'dotnet')
    def query(name, option):
        result = subprocess.run([str(dotnet), option], cwd=ROOT, env=clean_environment(),
                                stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=15, check=True)
        (logs / (name + '.log')).write_bytes(result.stdout)
        return result.stdout.decode('utf-8', errors='replace').strip()
    version = query('sdk-version', '--version')
    sdks = query('sdk-list', '--list-sdks')
    runtimes = query('runtime-list', '--list-runtimes')
    locations = [Path(match[2]) / version for line in sdks.splitlines()
                 if (match := re.fullmatch(r'(\S+) \[(.+)\]', line)) and match[1] == version]
    if len(locations) != 1:
        raise ValueError('Cannot identify the selected local SDK; nothing will be installed')
    sdk = no_links(locations[0])
    dotnet_root = sdk.parent.parent
    sdk_paths = [dotnet, sdk, dotnet_root / 'host/fxr', dotnet_root / 'sdk-manifests']
    for family in ('Microsoft.NETCore.App.Ref', 'Microsoft.AspNetCore.App.Ref', 'Microsoft.WindowsDesktop.App.Ref'):
        sdk_paths.append(dotnet_root / 'packs' / family)
    # The selected SDK executes on its installed major runtime. Include every
    # compatible candidate so installing another servicing runtime invalidates it.
    for family in ('Microsoft.NETCore.App', 'Microsoft.AspNetCore.App'):
        base = dotnet_root / 'shared' / family
        sdk_paths.extend(p for p in base.glob(version.split('.')[0] + '.*'))
    host_paths = [home / 'product-info.json', home / 'jbr', home / 'lib/ReSharperHost',
                  *sorted((home / 'lib').glob('*.jar'))]
    # MSBuild can import any environment variable as a property. Hash all of
    # the actual sanitized build environment, without writing credentials out.
    environment = clean_environment()
    nuget_configs = [Path.home() / '.nuget/NuGet',
                     Path(os.environ.get('APPDATA', Path.home())) / 'NuGet',
                     Path(os.environ.get('ProgramFiles(x86)', 'C:/Program Files (x86)')) / 'NuGet/Config']
    paths = [*sdk_paths, *host_paths, *nuget_configs]
    return paths, {'sdkVersion': version, 'dotnetRoot': str(dotnet_root), 'sdks': sdks, 'runtimes': runtimes,
                   'environmentSha256': digest(json.dumps(environment, sort_keys=True).encode()),
                   'toolchain': content_fingerprint(paths)}


def graph(projects, home):
    properties = {'RiderHome': home.as_posix(), 'AntlrJavaExec': (home / 'jbr/bin/java.exe').as_posix()}
    first = project_inputs(projects, properties=properties)
    csprojs = sorted({p for p in first.paths if p.suffix.lower() == '.csproj'})
    # No restore/evaluation here. Missing or unprovable evidence forces execution.
    assets = [p.parent / 'obj/project.assets.json' for p in csprojs]
    return project_inputs(projects, properties=properties, assets=assets,
                          antlr_tool=Path.home() / '.m2/antlr4-4.13.1-complete.jar'), csprojs


def build_observer(args, home, logs, output, payload_dir):
    work = output / ('work-' + uuid.uuid4().hex)
    work.mkdir()
    run('observer-backend', ['dotnet', 'build', RIDER_ROOT / 'tests/host/backend/HostObserver.csproj',
        '--configuration', 'Release', '--nologo', '--artifacts-path', work / 'dotnet', '--output', work / 'backend',
        '-p:RiderHome=' + home.as_posix(), '-p:AntlrJavaExec=' + (home / 'jbr/bin/java.exe').as_posix(),
        '-p:IncludeBoundaryAcceptance=' + str(args.boundaries).lower(),
        '-p:IncludeSourceBoundaryAcceptance=' + str(args.source_boundaries).lower()], logs, 110)
    classes = work / 'classes'
    classes.mkdir()
    jars = sorted((home / 'lib').glob('*.jar')) + sorted((sandbox.BOX / 'plugins/anotherfsm/lib').glob('*.jar'))
    options = ['-encoding', 'UTF-8', '-proc:none', '--release', '21', '-classpath',
               os.pathsep.join(path.as_posix() for path in jars), '-d', classes.as_posix(),
               *(source.as_posix() for source in sorted((RIDER_ROOT / 'tests/host/frontend').glob('*.java'))
                 if (args.boundaries or source.name not in ('ComplexBoundaryAcceptance.java', 'BoundaryHostStartup.java'))
                 and (args.source_boundaries or source.name not in ('SourceBoundaryAcceptance.java', 'SourceBoundaryStartup.java')))]
    compiler_args = work / 'javac.args'
    compiler_args.write_text(argfile(options), encoding='utf-8')
    run('observer-frontend', [home / 'jbr/bin/javac.exe', '-J-Duser.language=en', '@' + str(compiler_args)], logs, 90)
    entries = {path.relative_to(classes).as_posix(): path.read_bytes() for path in classes.rglob('*.class')}
    descriptor = (RIDER_ROOT / 'tests/host/frontend/plugin.xml').read_text(encoding='utf-8')
    if args.boundaries:
        startup = 'SourceBoundaryStartup' if args.source_boundaries else 'BoundaryHostStartup'
        descriptor = descriptor.replace('</extensions>', '<postStartupActivity implementation="anotherfsm.acceptance.' + startup + '"/></extensions>')
    entries['META-INF/plugin.xml'] = descriptor.encode('utf-8')
    for name, data in {'lib/acceptance.jar': zip_bytes(entries), 'dotnet/ReSharperPlugin.AnotherFSM.Acceptance.dll':
                       (work / 'backend/ReSharperPlugin.AnotherFSM.Acceptance.dll').read_bytes()}.items():
        path = no_links(payload_dir / name)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rider-home', type=Path, default=DEFAULT_RIDER)
    parser.add_argument('--case', default='all', help='Runtime case; never changes compilation payload')
    parser.add_argument('--force', '--no-cache', action='store_true', help='Execute all preparation phases without reuse')
    parser.add_argument('--launch', action='store_true', help='Explicitly run this controlled host test after preparation')
    parser.add_argument('--complex', action='store_true', help='Explicitly prepare the #34 controlled dependency/context fixture')
    parser.add_argument('--boundaries', action='store_true', help='Compile the independent complex boundary scenario driver (requires --complex)')
    parser.add_argument('--source-boundaries', action='store_true', help='Compile independent external-source policy test setup (requires --boundaries)')
    args = parser.parse_args()
    if args.source_boundaries and not args.boundaries:
        parser.error('--source-boundaries requires --boundaries')
    if args.boundaries and not args.complex:
        parser.error('--boundaries requires --complex')
    suite = 'source' if args.source_boundaries else 'boundary' if args.boundaries else 'complex' if args.complex else 'legacy'
    try:
        validate_case(suite, args.case)
    except ValueError as error:
        parser.error(str(error))
    home, _, launch = host(args.rider_home)
    # Controlled fixture/observer evidence; preparation still never launches by default.
    logs = no_links(ROOT / ('.scratch/issue-34/host' if args.complex else '.scratch/issue-33/host') / ('prepare-' + uuid.uuid4().hex))
    logs.mkdir(parents=True)
    output = managed_directory(RIDER_ROOT / 'build/host-tests')
    cache = managed_directory(RIDER_ROOT / 'build/acceptance-cache')
    tool_paths, common = compiler_environment(home, logs)
    tools = RIDER_ROOT / 'tools'
    implementation = [Path(__file__), tools / 'acceptance_cache.py', tools / 'build.py']
    phases = []

    def phase(name, inputs, outputs, configuration, execute, incomplete=()):
        plan = stage_plan(name, inputs=[*implementation, *inputs], outputs=outputs,
                          configuration={**common, **configuration}, incomplete=incomplete)
        result = cached_execute(cache, plan, execute, force=args.force)
        phases.append(result)
        (logs / 'phases.json').write_text(json.dumps(phases, indent=2) + '\n', encoding='utf-8')
        print(name + ': ' + result['status'] + ' (' + result['reason'] + ')')

    product, _ = graph([RIDER_ROOT / 'backend/AnotherFSM.Rider.csproj'], home)
    product_args = argparse.Namespace(rider_home=home, kotlin_compiler_dir=LOGS / 'cache', protocol_jar=None,
                                      frontend_dependency=[], backend_dependency=[], timeout=110)
    phase('product', [*product.paths, RIDER_ROOT / 'frontend', RIDER_ROOT / 'protocol',
                     tools / 'generate-protocol.py', LOGS / 'cache'], [DEFAULT_ZIP],
          {'configuration': 'Release', 'kotlinCompilerDir': str(product_args.kotlin_compiler_dir),
           'restoreEvidence': product.evidence_sha256},
          lambda: build(product_args, logs), product.incomplete)

    solution = RIDER_ROOT / ('tests/fixtures/boundary/Boundary.sln' if args.boundaries else
                              'fixtures/Complex/Complex.sln' if args.complex else 'fixtures/Acceptance.sln')
    projects = solution_projects(solution)
    if args.complex and not args.boundaries:
        projects += [RIDER_ROOT / 'fixtures/Complex' / name / (name + '.csproj')
                     for name in ('DllNodes', 'TwinLeft', 'TwinRight', 'PackageNodes')]
    fixtures, fixture_projects = graph(projects, home)
    fixture_inputs = [solution, *fixtures.paths]
    fixture_outputs = [p.parent / folder for p in fixture_projects for folder in ('bin', 'obj')]
    if args.complex and not args.boundaries:
        artifacts = RIDER_ROOT / 'fixtures/Complex/artifacts'
        # Produced DLL/feed/package files are output evidence, not self-inputs.
        fixture_inputs = [p for p in fixture_inputs if not p.resolve().is_relative_to(artifacts.resolve())]
        fixture_inputs.append(tools / 'prepare-complex-fixtures.py')
        for name, version in (('netstandard.library', '2.0.3'), ('microsoft.netcore.platforms', '1.1.0')):
            fixture_inputs.append(Path(common['dotnetRoot']) / 'sdk/NuGetFallbackFolder' / name / version)
        # The controlled script stages these existing local packages only.
        for name, version in (('microsoft.net.sdk.webassembly.pack', '10.0.9'),
                              ('microsoft.netcore.app.runtime.mono.browser-wasm', '10.0.9'),
                              ('microsoft.net.illink.tasks', '10.0.8'),
                              ('microsoft.net.illink.tasks', '8.0.27'),
                              ('microsoft.netcore.app.runtime.mono.browser-wasm', '8.0.28')):
            fixture_inputs.append(Path(os.environ.get('NUGET_PACKAGES', Path.home() / '.nuget/packages')) / name / version)
        fixture_outputs.append(artifacts)

    def prepare_fixture():
        if args.boundaries:
            binary = no_links(RIDER_ROOT / 'fixtures/Complex/artifacts/dll/Issue34.HostLoadedNodes.dll')
            if not binary.is_file():
                raise ValueError('Prepare the controlled Complex binary fixture first; no business assembly is loaded by this preparation')
            run('boundary-fixture-build', ['dotnet', 'build', solution, '--no-incremental',
                '--nologo', '-m:1', '-nr:false', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false'], logs, 110)
            run('boundary-net10-restore', ['dotnet', 'restore', RIDER_ROOT / 'tests/fixtures/boundary/Host/BoundaryHost.csproj',
                '--nologo', '-p:TargetFramework=net10.0', '-p:NuGetAudit=false'], logs, 110)
        elif args.complex:
            # Existing preparation uses incremental builds. On a miss, clean
            # only its known controlled projects so corrupted DLLs are rebuilt.
            for index, project in enumerate([solution, *projects[-4:]]):
                run('complex-clean-' + str(index), ['dotnet', 'clean', project, '--nologo', '-m:1', '-nr:false'], logs, 110)
            run('complex-fixture-prepare', [sys.executable, tools / 'prepare-complex-fixtures.py'], logs, 110)
        else:
            run('fixture-build', ['dotnet', 'build', solution, '--nologo', '--no-incremental',
                '-p:AntlrJavaExec=' + (home / 'jbr/bin/java.exe').as_posix()], logs, 110)
    phase('fixture-' + ('boundary' if args.boundaries else 'complex' if args.complex else 'legacy'),
          fixture_inputs, fixture_outputs, {'configuration': 'Debug', 'restoreEvidence': fixtures.evidence_sha256},
          prepare_fixture, fixtures.incomplete)

    # Always perform the existing guarded installation, including on cache hits.
    # A miss is never permission to overwrite an unowned/modified installation.
    sandbox.prepare(DEFAULT_ZIP, home, launch, logs)
    observer, _ = graph([RIDER_ROOT / 'tests/host/backend/HostObserver.csproj'], home)
    payload_dir = managed_directory(output / 'observer-payload')
    names = ('lib/acceptance.jar', 'dotnet/ReSharperPlugin.AnotherFSM.Acceptance.dll')
    phase('observer', [*observer.paths, RIDER_ROOT / 'tests/host/frontend',
                       sandbox.BOX / 'plugins/anotherfsm/lib', DEFAULT_ZIP],
          [payload_dir / name for name in names],
          {'configuration': 'Release', 'boundaries': args.boundaries, 'sourceBoundaries': args.source_boundaries,
           'restoreEvidence': observer.evidence_sha256},
          lambda: build_observer(args, home, logs, output, payload_dir), observer.incomplete)
    if content_fingerprint(tool_paths) != common['toolchain']:
        raise RuntimeError('Installed compiler/host inputs changed during preparation; prepare again')
    payload = {name: no_links(payload_dir / name).read_bytes() for name in names}
    expected = {name: digest(data) for name, data in payload.items()}
    with sandbox.operation():
        target = no_links(sandbox.BOX / 'plugins/anotherfsm-acceptance')
        state = no_links(sandbox.BOX / 'acceptance-installed.json')
        if target.exists():
            if not state.is_file() or inventory(target) != json.loads(state.read_text(encoding='utf-8')):
                raise ValueError('Refusing to replace an unowned/modified test observer')
            shutil.rmtree(target)
            if target.exists():
                raise RuntimeError('Owned observer directory was not removed')
        target.mkdir()
        for name, data in payload.items():
            path = target / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
        if inventory(target) != expected:
            raise RuntimeError('Test observer installation readback failed')
        state.write_text(json.dumps(expected, indent=2), encoding='utf-8')
    (logs / 'fixtures-before.json').write_text(json.dumps({str(path): digest(path.read_bytes())
        for path in (RIDER_ROOT / 'fixtures').rglob('*') if path.is_file() and path.suffix in ('.fsm', '.FSMScript', '.cs')
        and not any(part in ('bin', 'obj') for part in path.parts)}, indent=2), encoding='utf-8')
    record_preparation(sandbox.BOX, suite, case=args.case)
    summary = {'schemaVersion': 1, 'suite': suite, 'case': args.case, 'phases': phases, 'logs': str(logs)}
    with sandbox.operation():
        no_links(sandbox.BOX / 'acceptance-phases.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
    print('Prepared separate controlled test plugin for ' + suite + ' / ' + args.case + '.')
    if args.launch:
        (logs / 'launch').mkdir()
        sandbox.launch_sandbox(home, launch, logs / 'launch', acceptance=True, complex_contexts=args.complex, boundary_contexts=args.boundaries, source_boundaries=args.source_boundaries, acceptance_case=args.case)
    else:
        print('Run: python rider/tools/sandbox.py run --acceptance' + (' --boundary' if args.boundaries else ' --complex' if args.complex else '') + (' --source-boundaries' if args.source_boundaries else '') + (' --case ' + args.case if args.case != 'all' else ''))


if __name__ == '__main__':
    main()
