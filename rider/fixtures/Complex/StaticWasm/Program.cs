using FixtureSafety;

namespace Issue34.StaticWasm;

public static class Program
{
    // An accidental browser/host launch must write the configured marker and throw, not run a workflow.
    public static void Main() => Execution.Forbidden<object>("StaticWasm.Program.Main");
}
