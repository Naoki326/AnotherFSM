using FixtureSafety;
namespace Issue34.Net8WasmProbe;
public static class Program
{
    public static void Main() => Execution.Forbidden<object>("Net8WasmProbe.Program.Main");
}
