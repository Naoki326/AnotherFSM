"""Offline public-API tests: real dependency trees and byte-sized build outputs."""
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from acceptance_cache import cached_execute, project_inputs, stage_plan


class AcceptanceCacheTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.cache = self.root / 'cache'
        self.calls = []

    def file(self, name, data='input'):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(data, encoding='utf-8')
        return path

    def execute(self, name, inputs, *, config=None, incomplete=(), force=False):
        output = self.root / 'outputs' / name
        plan = stage_plan(name, inputs=inputs, outputs=[output],
                          configuration=config or {}, incomplete=incomplete)
        def build():
            self.calls.append(name)
            output.mkdir(parents=True, exist_ok=True)
            (output / 'payload').write_text(str(len(self.calls)), encoding='utf-8')
        return cached_execute(self.cache, plan, build, force=force)

    def test_compilers_do_not_inherit_volatile_agent_session_properties(self):
        from build import clean_environment
        environment = clean_environment({'PI_TOOL_CALL_ID': 'turn-one', 'PI_SESSION_FILE': 'private-session', 'MSBuildEnableWorkloadResolver': 'false'})
        self.assertNotIn('PI_TOOL_CALL_ID', environment)
        self.assertNotIn('PI_SESSION_FILE', environment)
        self.assertEqual('false', environment['MSBuildEnableWorkloadResolver'])

    def test_first_build_then_content_only_reuse_and_readable_receipt(self):
        source = self.file('src/code.cs')
        self.file('outputs/product/payload', 'untrusted existing build')
        first = self.execute('product', [source])
        self.assertEqual('executed', first['status'])
        source.touch()  # metadata-only changes are not build inputs
        second = self.execute('product', [source])
        self.assertEqual('cached', second['status'])
        self.assertEqual(first['inputSha256'], second['inputSha256'])
        self.assertEqual(first['outputSha256'], second['outputSha256'])
        receipt = json.loads(Path(second['receipt']).read_text(encoding='utf-8'))
        self.assertEqual(second['outputSha256'], receipt['outputSha256'])
        self.assertEqual(['product'], self.calls)
        source.write_text('changed', encoding='utf-8')
        self.assertEqual('executed', self.execute('product', [source])['status'])

    def test_observer_only_change_does_not_rebuild_product_or_fixture(self):
        product = self.file('product/code.cs')
        fixture = self.file('fixture/nodes.cs')
        observer = self.file('observer/Test.java')
        def prepare():
            return [self.execute('product', [product]),
                    self.execute('fixture', [fixture]),
                    self.execute('observer', [product, observer])]
        prepare()
        observer.write_text('new test', encoding='utf-8')
        self.assertEqual(['cached', 'cached', 'executed'], [r['status'] for r in prepare()])

    def test_missing_modified_and_extra_output_are_misses(self):
        source = self.file('code.cs')
        self.execute('product', [source])
        payload = self.root / 'outputs/product/payload'
        payload.write_text('tampered', encoding='utf-8')
        self.assertEqual('executed', self.execute('product', [source])['status'])
        payload.unlink()
        self.assertEqual('executed', self.execute('product', [source])['status'])
        self.file('outputs/product/extra', 'unexpected')
        self.assertEqual('executed', self.execute('product', [source])['status'])

    def test_sdk_configuration_and_real_dependency_bytes_are_inputs(self):
        source = self.file('code.cs')
        sdk = self.file('sdk/compiler.dll')
        package = self.file('packages/tool.nupkg')
        config = {'sdkVersion': '10.0.300', 'arguments': ['Release', '--release', '21']}
        self.execute('product', [source, sdk, package], config=config)
        self.assertEqual('cached', self.execute('product', [source, sdk, package], config=config)['status'])
        for dependency in (sdk, package):
            dependency.write_text('changed bytes', encoding='utf-8')
            self.assertEqual('executed', self.execute('product', [source, sdk, package], config=config)['status'])
        config['sdkVersion'] = '10.0.301'
        self.assertEqual('executed', self.execute('product', [source, sdk, package], config=config)['status'])
        config['arguments'][0] = 'Debug'
        self.assertEqual('executed', self.execute('product', [source, sdk, package], config=config)['status'])

    def test_real_transitive_project_linked_compile_and_ancestor_imports(self):
        top = self.file('src/A/A.csproj', '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../B/B.csproj" /></ItemGroup></Project>')
        self.file('src/B/B.csproj', '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../C/C.csproj" /></ItemGroup></Project>')
        self.file('src/C/C.csproj', '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><Compile Include="../shared/*.cs" /></ItemGroup></Project>')
        linked = self.file('src/shared/Linked.cs')
        props = self.file('src/Directory.Build.props', '<Project><PropertyGroup><Nullable>enable</Nullable></PropertyGroup></Project>')
        targets = self.file('src/Directory.Build.targets', '<Project />')
        code = self.file('src/C/Code.cs')
        def prepare():
            dependencies = project_inputs([top])
            self.assertFalse(dependencies.incomplete)
            return self.execute('product', dependencies.paths, incomplete=dependencies.incomplete)
        prepare()
        self.assertEqual('cached', prepare()['status'])
        for dependency in (linked, props, targets, code):
            dependency.write_text(dependency.read_text(encoding='utf-8') + '\n', encoding='utf-8')
            self.assertEqual('executed', prepare()['status'])
        self.file('src/shared/New.cs')
        self.assertEqual('executed', prepare()['status'])

    def test_unresolved_graph_is_explicit_conservative_miss(self):
        top = self.file('src/A.csproj', '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="$(Unknown)/Other.csproj" /></ItemGroup></Project>')
        deps = project_inputs([top])
        self.assertTrue(deps.incomplete)
        self.execute('product', deps.paths, incomplete=deps.incomplete)
        result = self.execute('product', deps.paths, incomplete=deps.incomplete)
        self.assertEqual('executed', result['status'])
        self.assertIn('incomplete', result['reason'])

    def test_external_msbuild_environment_imports_never_reuse_an_unproven_closure(self):
        top = self.file('src/App.csproj', '<Project Sdk="Microsoft.NET.Sdk" />')
        imported = self.file('outside/Custom.props', '<Project />')
        for variable in ('DirectoryBuildPropsPath', 'directorybuildtargetspath',
                         'DirectoryPackagesPropsPath', 'CustomBeforeMicrosoftCommonTargets',
                         'MSBuildExtensionsPath', 'RoslynTargetsPath'):
            with self.subTest(variable=variable):
                # Same real environment value throughout; only the external file bytes change.
                # The scanner cannot certify this implicit SDK import, so every attempt runs.
                environment = {variable: str(imported)}
                def prepare():
                    previous = os.environ.get(variable)
                    os.environ[variable] = str(imported)
                    try:
                        deps = project_inputs([top])
                    finally:
                        if previous is None:
                            del os.environ[variable]
                        else:
                            os.environ[variable] = previous
                    return self.execute('external-import', deps.paths, incomplete=deps.incomplete,
                                        config={'environment': environment})
                first = prepare()
                second = prepare()
                imported.write_text(imported.read_text(encoding='utf-8') + '\n', encoding='utf-8')
                third = prepare()
                self.assertEqual(['executed'] * 3, [item['status'] for item in (first, second, third)])
                self.assertIn('incomplete', second['reason'])
                self.assertIn('incomplete', third['reason'])

    def test_force_failed_execution_and_changed_input_never_bless_old_output(self):
        source = self.file('code.cs')
        self.execute('product', [source])
        self.assertEqual('executed', self.execute('product', [source], force=True)['status'])
        plan = stage_plan('product', inputs=[source], outputs=[self.root / 'outputs/product'])
        def failed():
            raise RuntimeError('compiler failed')
        with self.assertRaisesRegex(RuntimeError, 'compiler failed'):
            cached_execute(self.cache, plan, failed, force=True)
        self.assertEqual('executed', self.execute('product', [source])['status'])
        plan = stage_plan('product', inputs=[source], outputs=[self.root / 'outputs/product'])
        with self.assertRaisesRegex(RuntimeError, 'inputs changed'):
            cached_execute(self.cache, plan, lambda: source.write_text('racing edit'), force=True)

    def test_package_assets_binary_hint_and_import_changes_are_real_inputs(self):
        top = self.file('src/A.csproj', '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup>'
                        '<PackageReference Include="Test.Local" Version="1.0.0" />'
                        '<Reference Include="Nodes"><HintPath>../dll/Nodes.dll</HintPath></Reference>'
                        '</ItemGroup><Import Project="../config/Common.props" /></Project>')
        binary = self.file('dll/Nodes.dll', 'real small DLL input')
        imported = self.file('config/Common.props', '<Project />')
        config = self.file('feeds/NuGet.Config', '<configuration />')
        package = self.file('packages/test.local/1.0.0/lib/Library.dll', 'real package bytes')
        nupkg = self.file('packages/test.local/1.0.0/test.local.1.0.0.nupkg', 'real package archive input')
        asset = self.file('assets/project.assets.json', json.dumps({
            'packageFolders': {str(self.root / 'packages'): {}},
            'libraries': {'Test.Local/1.0.0': {'type': 'package', 'path': 'test.local/1.0.0'}},
            'project': {'restore': {'configFilePaths': [str(config)]}}}))
        def prepare():
            dependencies = project_inputs([top], assets=[asset])
            self.assertFalse(dependencies.incomplete)
            return self.execute('fixture', dependencies.paths, incomplete=dependencies.incomplete,
                                config={'restoreEvidence': dependencies.evidence_sha256})
        prepare()
        self.assertEqual('cached', prepare()['status'])
        for dependency in (binary, package, nupkg, config, imported):
            dependency.write_text(dependency.read_text() + '\n', encoding='utf-8')
            self.assertEqual('executed', prepare()['status'])
        asset.write_text(asset.read_text(encoding='utf-8') + '\n', encoding='utf-8')
        self.assertEqual('executed', prepare()['status'])
        self.file('packages/test.local/1.0.0/build/Custom.targets', '<Project><Target Name="Execute"><Exec Command="unknown" /></Target></Project>')
        self.assertTrue(project_inputs([top], assets=[asset]).incomplete)

    def test_imported_items_use_project_base_and_exists_conditions_are_inputs(self):
        top = self.file('src/App/App.csproj', '<Project Sdk="Microsoft.NET.Sdk"><Import Project="../../config/Common.props" /></Project>')
        self.file('config/Common.props', '<Project><ItemGroup><Compile Include="../Shared/Linked.cs" '
                  'Condition="Exists(\'../../switch/on\')" /></ItemGroup></Project>')
        linked = self.file('src/Shared/Linked.cs')
        def prepare():
            dependencies = project_inputs([top])
            self.assertFalse(dependencies.incomplete)
            self.assertIn(linked, dependencies.paths)
            return self.execute('product', dependencies.paths)
        prepare()
        self.assertEqual('cached', prepare()['status'])
        self.file('switch/on', 'enabled')
        self.assertEqual('executed', prepare()['status'])
        linked.write_text('new code', encoding='utf-8')
        self.assertEqual('executed', prepare()['status'])

    def test_case_is_not_a_compilation_input_and_stale_plan_is_rejected(self):
        source = self.file('src/code.cs')
        for case in ('all', 'generic-native', 'source-matrix'):
            result = self.execute('observer', [source], config={'suite': 'complex'})
            self.assertEqual('executed' if case == 'all' else 'cached', result['status'])
        plan = stage_plan('observer', inputs=[source], outputs=[self.root / 'outputs/observer'])
        source.write_text('edit after plan', encoding='utf-8')
        with self.assertRaisesRegex(RuntimeError, 'inputs changed since planning'):
            cached_execute(self.cache, plan, lambda: None)

    def test_unowned_cache_is_not_adopted(self):
        source = self.file('code.cs')
        self.cache.mkdir()
        with self.assertRaisesRegex(ValueError, 'unowned'):
            self.execute('product', [source])

    def test_links_are_rejected(self):
        source = self.file('code.cs')
        link = self.root / 'link'
        try:
            link.symlink_to(source)
        except OSError:
            self.skipTest('symlink privilege unavailable')
        with self.assertRaisesRegex(ValueError, 'linked'):
            self.execute('product', [link])


if __name__ == '__main__':
    unittest.main()
