# Native trust refusal fixture

NEVER build, restore, run, or click Trust and Open for this solution. Use only the isolated sandbox `run --trust-probe`, observe the real native prompt, then choose Don't Open. Its MSBuild InitialTargets writes `unexpected-msbuild.marker` and fails if execution is accidentally requested; verify marker absence before and after refusal. This is not part of any solution/build suite. Local DSL/no-host behavior is independently tested in the trusted boundary suite, including actual consumer-project Unload.
