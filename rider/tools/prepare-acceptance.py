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
    args = parser.parse_args()
    home, _, launch = host(args.rider_home)
    # Issue #33 controlled fixture/observer evidence; preparation still never launches by default.
    logs = no_links(ROOT / '.scratch/issue-33/host' / ('prepare-' + uuid.uuid4().hex))
    logs.mkdir(parents=True)
    output = managed_directory(RIDER_ROOT / 'build/host-tests')
    work = output / ('work-' + uuid.uuid4().hex)
    work.mkdir()
    run('fixture-build', ['dotnet', 'build', RIDER_ROOT / 'fixtures/Acceptance.sln', '--nologo',
        '-p:AntlrJavaExec=' + (home / 'jbr/bin/java.exe').as_posix()], logs, 110)
    sandbox.prepare(DEFAULT_ZIP, home, launch, logs)
    run('observer-backend', ['dotnet', 'build', RIDER_ROOT / 'tests/host/backend/HostObserver.csproj',
        '--configuration', 'Release', '--nologo', '--artifacts-path', work / 'dotnet', '--output', work / 'backend',
        '-p:RiderHome=' + home.as_posix(), '-p:AntlrJavaExec=' + (home / 'jbr/bin/java.exe').as_posix()], logs, 110)
    classes = work / 'classes'
    classes.mkdir()
    jars = sorted((home / 'lib').glob('*.jar')) + sorted((sandbox.BOX / 'plugins/anotherfsm/lib').glob('*.jar'))
    options = ['-encoding', 'UTF-8', '-proc:none', '--release', '21', '-classpath',
               os.pathsep.join(path.as_posix() for path in jars), '-d', classes.as_posix(),
               *(source.as_posix() for source in sorted((RIDER_ROOT / 'tests/host/frontend').glob('*.java')))]
    compiler_args = work / 'javac.args'
    compiler_args.write_text(argfile(options), encoding='utf-8')
    run('observer-frontend', [home / 'jbr/bin/javac.exe', '-J-Duser.language=en', '@' + str(compiler_args)], logs, 90)
    entries = {path.relative_to(classes).as_posix(): path.read_bytes() for path in classes.rglob('*.class')}
    entries['META-INF/plugin.xml'] = (RIDER_ROOT / 'tests/host/frontend/plugin.xml').read_bytes()
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
    print('Prepared separate controlled test plugin. Product ZIP bytes are unchanged.')
    if args.launch:
        sandbox.launch_sandbox(home, launch, log_directory('sandbox-host-test'), acceptance=True)
    else:
        print('Run: python rider/tools/sandbox.py run --acceptance')


if __name__ == '__main__':
    main()
