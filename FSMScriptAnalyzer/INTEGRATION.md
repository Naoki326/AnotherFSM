# Script generator integration

`.FSMScript` files generate `ScriptText`, `ModuleScripts`, `Create(factory)`,
`Engine`, and accessors for ordinary node/event declarations. Both `def N(Type)`
and `def N as Type state` are supported. DSL Namespace prefixes are part of the
lookup key; dots become underscores in C# property names.

Module DSL uses explicit instances and public interfaces:

```text
module Checker {
    input Entry;
    output Done;
    def Entry(Step) { success -> Done; }
}
use Checker as First;
use Checker as Second { Pos:(10,20); Color:"blue"; FlowID:second; }
def Start(Step) { success -> Launch; }
def Finish(Step) { }
Launch -> Start to First;
First.Done -> Second;
Second.Done -> Finish;
```

For imported modules, include the main script and every transitive `.fsm` file
as MSBuild `AdditionalFiles`. Imports resolve relative to the importing file,
not by a project-wide basename search. The generator embeds the original text
and registers it under the import name before initializing Engine. Import
aliases, module-header aliases, and files without a module wrapper remain
Engine concerns. No runtime copy of these embedded files is required.

The generator has no core or ANTLR assembly dependency in the Roslyn analyzer
host. Consumers reference the core runtime separately. A project reference to
the generator uses `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`;
the NuGet package carries its DLL under `analyzers/dotnet/cs`.

## Declaration projection, not another module compiler

The scanner recognizes token and scope boundaries, comments, strings, ordinary
branches/transitions, and event declarations. It skips module template bodies
and `use` metadata bodies when creating accessors. Module instances are visual
proxies, not runtime nodes. Expanded nodes/events and public Outputs are queried
through `flow.Engine.GetNode("First.Entry")` and
`flow.Engine.GetEvent("First.Done")`; no module-specific typed API is generated.
In particular, the generator does not guess a Namespace for an Output reference
or interpret ordinary dotted event/node names as module members.

Engine still parses the untouched scripts, validates interfaces, expands
instances, and resolves all connections. This intentionally does not preserve
old module `def` instances, instance-body event mappings, `terminal`, or module
instances used as placeholder source nodes. Use `use Module as Instance;` and
`Instance.Output -> Target;` instead. Ordinary scripts need no migration.

## Integration validation

`tests/FSMScriptAnalyzerTest` is a real analyzer consumer, not a generated-text
snapshot. Its assertions initialize generated classes, exercise every public
node/event getter, query expanded objects and connections, and execute generated
module flows to completion. Any assertion/initialization failure exits nonzero.

```sh
dotnet run --project tests/FSMScriptAnalyzerTest/FSMScriptAnalyzerTest.csproj --no-launch-profile -p:NuGetAudit=false
```
