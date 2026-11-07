#!/usr/bin/env python3
"""Prepare only controlled fixtures and a separate read-only host test plugin."""
import argparse
import json
import os
from pathlib import Path
import shutil
import sys
import uuid

sys.dont_write_bytecode = True
from build import (ROOT, RIDER_ROOT, DEFAULT_RIDER, DEFAULT_ZIP, host, run, log_directory,
                   managed_directory, zip_bytes, argfile, digest, no_links)
import sandbox
from acceptance_contract import record_preparation


def inventory(directory):
    result = {}
    for path in directory.rglob('*'):
        no_links(path)
        if path.is_file():
            result[path.relative_to(directory).as_posix()] = digest(path.read_bytes())
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--rider-home', type=Path, default=DEFAULT_RIDER)
    parser.add_argument('--launch', action='store_true', help='Explicitly run this controlled host test after preparation')
    parser.add_argument('--complex', action='store_true', help='Explicitly prepare the #34 controlled dependency/context fixture')
    parser.add_argument('--boundaries', action='store_true', help='Compile the independent complex boundary scenario driver (requires --complex)')
    parser.add_argument('--source-boundaries', action='store_true', help='Compile independent external-source policy test setup (requires --boundaries)')
    args = parser.parse_args()
    if args.source_boundaries and not args.boundaries:
        parser.error('--source-boundaries requires --boundaries')
    if args.boundaries and not args.complex:
        parser.error('--boundaries requires --complex')
    home, _, launch = host(args.rider_home)
    # Controlled fixture/observer evidence; preparation still never launches by default.
    logs = no_links(ROOT / ('.scratch/issue-34/host' if args.complex else '.scratch/issue-33/host') / ('prepare-' + uuid.uuid4().hex))
    logs.mkdir(parents=True)
    output = managed_directory(RIDER_ROOT / 'build/host-tests')
    work = output / ('work-' + uuid.uuid4().hex)
    work.mkdir()
    if args.boundaries:
        binary = RIDER_ROOT / 'fixtures/Complex/artifacts/dll/Issue34.HostLoadedNodes.dll'
        if not binary.is_file():
            raise ValueError('Prepare the controlled Complex binary fixture first; no business assembly is loaded by this preparation')
        run('boundary-fixture-build', ['dotnet', 'build', RIDER_ROOT / 'tests/fixtures/boundary/Boundary.sln',
            '--nologo', '-m:1', '-nr:false', '-p:UseSharedCompilation=false', '-p:NuGetAudit=false'], logs, 110)
        run('boundary-net10-restore', ['dotnet', 'restore', RIDER_ROOT / 'tests/fixtures/boundary/Host/BoundaryHost.csproj',
            '--nologo', '-p:TargetFramework=net10.0', '-p:NuGetAudit=false'], logs, 110)
    elif args.complex:
        run('complex-fixture-prepare', [sys.executable, RIDER_ROOT / 'tools/prepare-complex-fixtures.py'], logs, 110)
    else:
        run('fixture-build', ['dotnet', 'build', RIDER_ROOT / 'fixtures/Acceptance.sln', '--nologo',
            '-p:AntlrJavaExec=' + (home / 'jbr/bin/java.exe').as_posix()], logs, 110)
    sandbox.prepare(DEFAULT_ZIP, home, launch, logs)
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
    payload = {'lib/acceptance.jar': zip_bytes(entries), 'dotnet/ReSharperPlugin.AnotherFSM.Acceptance.dll':
               (work / 'backend/ReSharperPlugin.AnotherFSM.Acceptance.dll').read_bytes()}
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
    suite = 'source' if args.source_boundaries else 'boundary' if args.boundaries else 'complex' if args.complex else 'legacy'
    record_preparation(sandbox.BOX, suite)
    print('Prepared separate controlled test plugin for ' + suite + '. Product ZIP bytes are unchanged.')
    if args.launch:
        (logs / 'launch').mkdir()
        sandbox.launch_sandbox(home, launch, logs / 'launch', acceptance=True, complex_contexts=args.complex, boundary_contexts=args.boundaries, source_boundaries=args.source_boundaries)
    else:
        print('Run: python rider/tools/sandbox.py run --acceptance' + (' --boundary' if args.boundaries else ' --complex' if args.complex else '') + (' --source-boundaries' if args.source_boundaries else ''))


if __name__ == '__main__':
    main()
