using FixtureSafety;

namespace Issue34.MatrixWasm;

public static class Program
{
    // Compile only. An accidental launch must trip the same central execution sentinel.
    public static void Main() => Execution.Forbidden<object>("MatrixWasm.Program.Main");
}
