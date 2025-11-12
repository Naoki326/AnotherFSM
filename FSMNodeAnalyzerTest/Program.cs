using System;
using System.Threading.Tasks;
using StateMachine;
using System.Collections.Generic;

namespace FSMNodeAnalyzerTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== 测试 SourceIndex 查询 ===");
            
            try
            {
                // 使用 SourceIndex 查询已生成的类型 -> 源文件路径映射
                var pathTestNode = TestNode.StaticSourceCSPath;
                Console.WriteLine($"TestNode 源文件: {pathTestNode ?? "<未注册>"}");

                var fullNameAnother = typeof(AnotherTestNode).FullName!;
                var pathAnother = AnotherTestNode.StaticSourceCSPath;
                Console.WriteLine($"{fullNameAnother} 源文件: {pathAnother ?? "<未注册>"}");

                Console.WriteLine("\n=== 生成器事件字段输出 ===");
                var fields = typeof(TestNode).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                foreach (var f in fields)
                {
                    if (f.Name.StartsWith("Event_"))
                    {
                        Console.WriteLine($"TestNode.{f.Name} = {f.GetValue(null)}");
                    }
                }
                var evListField = typeof(TestNode).GetField("Events", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (evListField != null && evListField.GetValue(null) is System.Collections.Generic.IReadOnlyList<int> testEvents)
                {
                    Console.WriteLine("TestNode.Events (int, distinct):");
                    foreach (var item in testEvents) Console.WriteLine($"  - {item}");
                }
                var fields2 = typeof(AnotherTestNode).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                foreach (var f in fields2)
                {
                    if (f.Name.StartsWith("Event_"))
                    {
                        Console.WriteLine($"AnotherTestNode.{f.Name} = {f.GetValue(null)}");
                    }
                }
                var evListField2 = typeof(AnotherTestNode).GetField("Events", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (evListField2 != null && evListField2.GetValue(null) is System.Collections.Generic.IReadOnlyList<int> anotherEvents)
                {
                    Console.WriteLine("AnotherTestNode.Events (int, distinct):");
                    foreach (var item in anotherEvents) Console.WriteLine($"  - {item}");
                }

                Console.WriteLine("\n=== 测试完成 ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }

    public enum TestEnum
    {
        Next, Previous
    }

    // 测试类 - 派生自AbstractFSMNode
    public partial class TestNode : AsyncEnumFSMNode
    {
        protected override async IAsyncEnumerable<object> ExecuteEnumerable()
        {
            yield return Yield.Event(FSMEnum.Next, false);
            yield return Yield.Event(TestEnum.Previous);
            yield return Yield.Event(2);
            yield return Yield.Next;
        }
        
    }

    // 另一个测试类
    public partial class AnotherTestNode : AsyncEnumFSMNode
    {
        protected override async IAsyncEnumerable<object> ExecuteEnumerable()
        {
            yield return Yield.Event(FSMEnum.Failed);
            yield return Yield.Error;
            yield return Yield.Break;
            await Task.CompletedTask;
        }
    }
}
