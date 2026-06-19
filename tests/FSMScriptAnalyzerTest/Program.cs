using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using StateMachine;

namespace FSMScriptAnalyzerTest
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== FSM 模块化系统 Demo 测试 ===\n");

            var factory = new ReflectionNodeFactory(
                new[]
                {
                    typeof(StateMachine.IFSMNode).Assembly,
                    typeof(StartNode).Assembly,
                    Assembly.GetExecutingAssembly(),
                });

            await Test1_ModuleViaApi(factory);
            await Test2_MultipleInstances(factory);
            await Test3_FluentApi(factory);

            Test4_ErrorPaths(factory);
            Test5_TransformWithModule(factory);
            Test6_DuplicateRegistration(factory);
            await Test7_ModuleTerminalExternalTransitions(factory);
            await Test8_UserCheckerScriptWithDemoNodes(factory);
            await Test9_TerminalFallbackBug(factory);

            Test10_TerminalFallbackRejectedScript(factory);
            Test11_TerminalFallbackRejectedFluent(factory);
            Test12_OutputMapEventNameCollisionRejected(factory);

            Console.WriteLine("\n=== 全部测试完成 ===");
        }

        // ================================================================
        // Test 1: 模块注册 + 通过脚本 import 使用
        // ================================================================
        static async Task Test1_ModuleViaApi(ReflectionNodeFactory factory)
        {
            Console.WriteLine("--- Test 1: 模块注册 + API 使用 ---");

            var moduleScript = """
                module SubFlow {
                    input Check;
                    output DoneEvent;
                    output Failed;

                    def Check(TestAcc)
                    {
                        1->NextEvent;
                        3->Failed;
                        Pos:(100, 200);
                        Color: "light-blue";
                    }

                    def Process(TestIdle)
                    {
                        1->NextEvent;
                        Pos:(300, 200);
                        Color: "green";
                    }

                    def EndNode(TestEnd)
                    {
                        1->DoneEvent;
                        Pos:(500, 200);
                        Color: "red";
                    }

                    def NextEvent as event;
                    def DoneEvent as event;
                    def Failed as event;

                    NextEvent->Check to Process;
                    NextEvent->Process to EndNode;
                }
            """;

            var mainScript = """
                import SubFlow;

                def Sub1(SubFlow)
                {
                    0->GoEvent;
                    1->ErrorEvent;
                    Pos:(0, 0);
                    Color: "white";
                }

                def Start(TestStart)
                {
                    1->StartEvent;
                    Pos:(-200, 100);
                    Color: "red";
                }

                def Finish(TestEnd)
                {
                    1->EndEvent;
                    Pos:(600, 100);
                    Color: "indigo";
                }

                def Error(TestEnd)
                {
                    1->ErrorEndEvent;
                    Pos:(200, 400);
                    Color: "red";
                }

                                StartEvent->Start to Sub1.Check;
                GoEvent->Sub1 to Finish;
                ErrorEvent->Sub1 to Error;
            """;

            try
            {
                var engine = new FSMEngine(factory);
                engine.RegisterModule("SubFlow", moduleScript);
                Console.WriteLine("  [OK] 模块 SubFlow 已注册");

                engine.CreateStateMachine(mainScript);
                Console.WriteLine("  [OK] 状态机已创建");

                Console.WriteLine("  节点列表:");
                foreach (var name in engine.GetNodeNames().OrderBy(n => n))
                    Console.WriteLine($"    - {name}");

                Console.WriteLine("  事件列表:");
                foreach (var evt in engine.GetEventNames().OrderBy(e => e))
                    Console.WriteLine($"    - {evt}");

                Console.WriteLine($"  模块实例数: {engine.ModuleInstances.Count}");
                foreach (var kv in engine.ModuleInstances)
                {
                    var inst = kv.Value;
                    Console.WriteLine($"    实例 {inst.InstanceName}: OutputMap=[{string.Join(", ", inst.OutputEventMap.Select(p => $"{p.Key}->{p.Value}"))}], ExternalEventToInternalNodes=[{string.Join(", ", inst.ExternalEventToInternalNodes.Select(p => $"{p.Key}=>{string.Join(",", p.Value)}"))}]");
                }

                // 执行状态机
                Console.WriteLine("  执行状态机...");
                if (engine.TryGetNode("Start", out IFSMNode startNode) &&
                    engine.TryGetEvent("EndEvent", out FSMEvent endEvent))
                {
                    var executor = new FSMExecutor(startNode, endEvent);
                    executor.NodeStateChanged += (sender, nodeName) =>
                        Console.WriteLine($"    进入节点: {nodeName}");
                    executor.NodeExitChanged += (sender, nodeName) =>
                        Console.WriteLine($"    离开节点: {nodeName}");

                    bool ok = await executor.RestartAsync(true);
                    Console.WriteLine($"    Restart 结果: {(ok ? "成功" : "失败")}");
                    try { await Task.WhenAny(executor.ExecutorTask, Task.Delay(15000)); }
                    catch { }
                    Console.WriteLine($"    最终状态: {executor.State}");
                }

                Console.WriteLine("  Test 1 通过\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Test 1 失败: {ex.Message}\n{ex.StackTrace}\n");
            }
        }

        // ================================================================
        // Test 2: 同一个模块多次实例化
        // ================================================================
        static async Task Test2_MultipleInstances(ReflectionNodeFactory factory)
        {
            Console.WriteLine("--- Test 2: 同一模块多次实例化 ---");

            var moduleScript = """
                module Checker {
                    input Entry;
                    output OKEvent;

                    def Entry(TestIdle)
                    {
                        1->OKEvent;
                        Pos:(100, 100);
                        Color: "light-blue";
                    }

                    def OKEvent as event;
                }
            """;

            var mainScript = """
                import Checker;

                def Checker1(Checker)
                {
                    0->Check1OK;
                    Pos:(0, 0);
                    Color: "white";
                }

                def Checker2(Checker)
                {
                    0->Check2OK;
                    Pos:(0, 200);
                    Color: "white";
                }

                def Start(TestStart)
                {
                    1->StartEvent;
                    Pos:(-200, 100);
                    Color: "red";
                }

                def End(TestEnd)
                {
                    1->FinalEvent;
                    Pos:(400, 100);
                    Color: "indigo";
                }

                StartEvent->Start to Checker1.Entry;
                Check1OK->Checker1 to End;
                Check2OK->Checker2 to End;
            """;

            try
            {
                var engine = new FSMEngine(factory);
                engine.RegisterModule("Checker", moduleScript);
                engine.CreateStateMachine(mainScript);

                Console.WriteLine("  节点列表:");
                foreach (var name in engine.GetNodeNames().OrderBy(n => n))
                    Console.WriteLine($"    - {name}");

                var checker1 = engine.ModuleInstances["Checker1"];
                var checker2 = engine.ModuleInstances["Checker2"];
                Console.WriteLine($"  Checker1 OutputMap: OKEvent -> {checker1.OutputEventMap["OKEvent"]}");
                Console.WriteLine($"  Checker2 OutputMap: OKEvent -> {checker2.OutputEventMap["OKEvent"]}");

                if (engine.GetEventNames().Contains("Check1OK") &&
                    engine.GetEventNames().Contains("Check2OK"))
                    Console.WriteLine("  [OK] 两个实例的 output 映射到不同外部事件");

                Console.WriteLine("  Test 2 通过\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Test 2 失败: {ex.Message}\n{ex.StackTrace}\n");
            }
        }

        // ================================================================
        // Test 3: 嵌套模块 import
        // ================================================================
        static async Task Test3_FluentApi(ReflectionNodeFactory factory)
        {
            Console.WriteLine("--- Test 3: Fluent API 使用模块 ---");

            var moduleScript = """
                module Validator {
                    input Start;
                    output Passed, Rejected;

                    def Start(TestIdle)
                    {
                        1->Passed;
                        3->Rejected;
                        Pos:(100, 100);
                        Color: "green";
                    }

                    def Passed as event;
                    def Rejected as event;
                }
            """;

            try
            {
                var engine = FSMEngineBuilder.Create()
                    .ConfigureNodeFactory(factory)
                    .ConfigureModule("Validator", moduleScript)
                    .ConfigureFSMDefine(def => def
                        .AddNode<TestStartNode>("MainStart")
                        .AddNode<TestEndNode>("MainOK")
                        .AddNode<TestEndNode>("MainFail")
                        .AddModule("Validator", "Val1", mod => mod
                            .MapOutput("Passed", "OKEvent")
                            .MapOutput("Rejected", "FailEvent")
                            .SetPosition(0, 0)
                            .SetColor("white"))
                        .AddConnection("StartEvent", "MainStart", "Val1.Start")
                        .AddConnection("OKEvent", "Val1", "MainOK")
                        .AddConnection("FailEvent", "Val1", "MainFail")
                    )
                    .Build();

                Console.WriteLine("  [OK] Fluent API 构建成功");

                Console.WriteLine("  节点列表:");
                foreach (var name in engine.GetNodeNames().OrderBy(n => n))
                    Console.WriteLine($"    - {name}");

                Console.WriteLine("  事件列表:");
                foreach (var evt in engine.GetEventNames().OrderBy(e => e))
                    Console.WriteLine($"    - {evt}");

                Console.WriteLine("  Test 3 通过\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Test 3 失败: {ex.Message}\n{ex.StackTrace}\n");
            }
        }

        // ================================================================
        // Test 4: 错误路径 — 无效 output 索引
        // ================================================================
        static void Test4_ErrorPaths(ReflectionNodeFactory factory)
        {
            Console.WriteLine("--- Test 4: 错误路径 ---");

            var moduleScript = """
                module ErrMod {
                    input Entry;
                    output OK;

                    def Entry(TestIdle)
                    {
                        1->OK;
                        Pos:(100, 100);
                        Color: "green";
                    }

                    def OK as event;
                }
            """;

            // 测试 4a: 无效 output 索引应抛异常
            var mainScriptBadIndex = """
                import ErrMod;

                def Inst(ErrMod)
                {
                    5->BadEvent;
                }

                def Start(TestStart) { 1->StartEvent; }
                def End(TestEnd) { 1->EndEvent; }
                StartEvent->Start to Inst.Entry;
                BadEvent->Inst to End;
            """;

            try
            {
                var engine = new FSMEngine(factory);
                engine.RegisterModule("ErrMod", moduleScript);
                engine.CreateStateMachine(mainScriptBadIndex);
                Console.WriteLine("  Test 4a 失败: 应该抛出异常但未抛出");
            }
            catch (ScriptException ex) when (ex.Message.Contains("output 映射出错"))
            {
                Console.WriteLine("  [OK] Test 4a: 无效输出索引正确抛出异常");
            }

            // 测试 4b: 重复注册模块应幂等（不抛异常）
            try
            {
                var engine = new FSMEngine(factory);
                engine.RegisterModule("ErrMod", moduleScript);
                engine.RegisterModule("ErrMod", moduleScript);
                Console.WriteLine("  [OK] Test 4b: 重复注册模块幂等");
            }
            catch
            {
                Console.WriteLine("  Test 4b 失败: 重复注册不应抛异常");
            }

            Console.WriteLine("  Test 4 完成\n");
        }

        // ================================================================
        // Test 5: Transform 后模块实例被清理
        // ================================================================
        static void Test5_TransformWithModule(ReflectionNodeFactory factory)
        {
            Console.WriteLine("--- Test 5: Transform + 模块实例 ---");

            var moduleScript = """
                module TMod {
                    input Entry;
                    output Done;

                    def Entry(TestIdle)
                    {
                        1->Done;
                        Pos:(100, 100);
                        Color: "green";
                    }

                    def Done as event;
                }
            """;

            var mainScript = """
                import TMod;

                def Inst(TMod)
                {
                    0->GoEvent;
                }

                def Start(TestStart) { 1->StartEvent; }
                def End(TestEnd) { 1->EndEvent; }
                StartEvent->Start to Inst.Entry;
                GoEvent->Inst to End;
            """;

            var transformScript = """
                GoEvent->Start to End;
            """;

            try
            {
                var engine = new FSMEngine(factory);
                engine.RegisterModule("TMod", moduleScript);
                engine.CreateStateMachine(mainScript);
                Console.WriteLine($"  初次构建 moduleInstances 数量: {engine.ModuleInstances.Count}");

                engine.Transform(transformScript);
                Console.WriteLine($"  Transform 后 moduleInstances 数量: {engine.ModuleInstances.Count}");

                if (engine.ModuleInstances.Count > 0)
                    Console.WriteLine("  [OK] Transform 后模块实例保留");
                else
                    Console.WriteLine("  Test 5 失败: Transform 后模块实例不应被清理");

                // 验证新连线生效
                if (engine.TryGetNode("Start", out IFSMNode startNode) &&
                    startNode.HasTransition(engine.GetEvent("GoEvent")))
                    Console.WriteLine("  [OK] Transform 新连线生效");

                Console.WriteLine("  Test 5 通过\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Test 5 失败: {ex.Message}\n");
            }
        }

        // ================================================================
        // Test 6: 重复注册幂等
        // ================================================================
        static void Test6_DuplicateRegistration(ReflectionNodeFactory factory)
        {
            Console.WriteLine("--- Test 6: 重复注册幂等 ---");

            var moduleScript = """
                module DupMod {
                    input Entry;
                    output Done;
                    def Entry(TestIdle)
                    { 1->Done; Pos:(100, 100); Color: "green"; }
                    def Done as event;
                }
            """;

            var mainScript = """
                import DupMod;

                def Inst(DupMod)
                {
                    0->GoEvent;
                }

                def Start(TestStart) { 1->StartEvent; }
                def End(TestEnd) { 1->EndEvent; }
                StartEvent->Start to Inst.Entry;
                GoEvent->Inst to End;
            """;

            try
            {
                var engine = new FSMEngine(factory);
                // 重复注册同一模块应不抛异常
                engine.RegisterModule("DupMod", moduleScript);
                engine.RegisterModule("DupMod", moduleScript);
                Console.WriteLine("  [OK] 重复注册幂等");
                engine.CreateStateMachine(mainScript);
                Console.WriteLine("  [OK] 重复注册后状态机创建正常");

                Console.WriteLine("  节点列表:");
                foreach (var name in engine.GetNodeNames().OrderBy(n => n))
                    Console.WriteLine($"    - {name}");

                Console.WriteLine("  Test 6 通过\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Test 6 失败: {ex.Message}\n");
            }
        }

        // ================================================================
        // Test 7: terminal 节点作为模块对外出口，外部事件不绑定模块 output
        // ================================================================
        static async Task Test7_ModuleTerminalExternalTransitions(ReflectionNodeFactory factory)
        {
            Console.WriteLine("--- Test 7: module terminal 外部连线 ---");

            var script = """
                module Checker {
                    input Entry;
                    output OK, NG;
                    terminal Entry, Exit;

                    def Entry(TestIdle)
                    {
                        1->InternalDone;
                        3->NG;
                        Pos:(100, 100);
                        Color: "light-blue";
                    }

                    def Exit(TestEnd)
                    {
                        1->OK;
                        Pos:(300, 100);
                        Color: "green";
                    }

                    def InternalDone as event;
                    def OK as event;
                    def NG as event;

                    InternalDone->Entry to Exit;
                }

                import Checker;

                def Start(TestStart)
                {
                    1->Launch;
                    Pos:(-100, 100);
                    Color: "red";
                }

                def CheckA(Checker)
                {
                    OK->CheckAOK;
                    NG->CheckANG;
                    Pos:(100, 100);
                    Color: "white";
                }

                def Done(TestEnd)
                {
                    1->Finished;
                    Pos:(500, 100);
                    Color: "indigo";
                }

                def Error(TestEnd)
                {
                    1->ErrorFinished;
                    Pos:(500, 300);
                    Color: "red";
                }

                Launch->Start to CheckA;
                CheckAOK->CheckA to Done;
                CheckANG->CheckA to Error;
            """;

            try
            {
                var engine = new FSMEngine(factory);
                engine.CreateStateMachine(script);

                var startTransition = engine["Start"].GetFSMTransitions()
                    .SingleOrDefault(t => t.Trigger.EventName == "Launch");
                if (startTransition == null || startTransition.Target.Name != "CheckA.Entry")
                    throw new Exception("Launch 没有展开到 CheckA.Entry");

                var terminalTransition = engine["CheckA.Exit"].GetFSMTransitions()
                    .SingleOrDefault(t => t.Trigger.EventName == "CheckAOK");
                if (terminalTransition == null || terminalTransition.Target.Name != "Done")
                    throw new Exception("CheckAOK 没有从 CheckA.Exit 连到 Done");

                var ngTransition = engine["CheckA.Entry"].GetFSMTransitions()
                    .SingleOrDefault(t => t.Trigger.EventName == "CheckANG");
                if (ngTransition == null || ngTransition.Target.Name != "Error")
                    throw new Exception("CheckANG 没有从 CheckA.Entry 连到 Error");

                if (!engine.ModuleInstances.TryGetValue("CheckA", out var instance))
                    throw new Exception("模块实例 CheckA 不存在");
                if (!instance.InputNodeNames.Contains("CheckA.Entry"))
                    throw new Exception("CheckA input 节点没有记录");
                if (!instance.TerminalNodeNames.Contains("CheckA.Exit"))
                    throw new Exception("CheckA terminal 节点没有记录");

                var visited = new List<string>();
                if (!engine.TryGetNode("Start", out IFSMNode startNode))
                    throw new Exception("Start 节点不存在");
                if (!engine.TryGetEvent("Finished", out FSMEvent endEvent))
                    throw new Exception("Finished 事件不存在");

                var executor = new FSMExecutor(startNode, endEvent);
                executor.NodeStateChanged += (_, nodeName) => visited.Add(nodeName);
                await executor.RestartAsync(true);
                await Task.WhenAny(executor.ExecutorTask, Task.Delay(3000));

                if (executor.State != FSMState.Finished)
                    throw new Exception($"执行没有完成，当前状态: {executor.State}, visited=[{string.Join(" -> ", visited)}]");
                if (!visited.Contains("Done"))
                    throw new Exception($"执行没有进入 Done，visited=[{string.Join(" -> ", visited)}]");

                Console.WriteLine("  [OK] module 目标展开到 input");
                Console.WriteLine("  [OK] OK 映射到 CheckAOK 后从 Exit terminal 出线");
                Console.WriteLine("  [OK] NG 映射到 CheckANG 后从 Entry terminal 出线");
                Console.WriteLine("  [OK] executor 能从 terminal 继续到 module 外部节点");
                Console.WriteLine("  Test 7 通过\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Test 7 失败: {ex.Message}\n");
                throw;
            }
        }

        // ================================================================
        // Test 8: 用户复现 — terminal + named output mapping + DemoNodes
        // ================================================================
        static async Task Test8_UserCheckerScriptWithDemoNodes(ReflectionNodeFactory factory)
        {
            Console.WriteLine("--- Test 8: 用户 Checker 脚本执行路径 ---");

            var script = """
                module Checker {
                    input Entry;
                    output OK, NG;
                    terminal Exit, Entry;

                    def Entry(Accumulate)
                    {
                        1->NextEvent;
                        3->NG;
                        Pos:(100, 200);
                        Color: "light-blue";
                        Type: Accumulate;
                        FlowID: 6282938a-22c3-422a-b702-459ac2dffb99;
                    }

                    def Exit(Idle)
                    {
                        1->OK;
                        Pos:(450, 255);
                        Color: "green";
                        Type: Idle;
                        FlowID: 73b2164d-0aac-4598-be1d-20c4d7797c91;
                    }

                    def NextEvent as event;
                    def NG as event;
                    def OK as event;
                    NextEvent->Entry to Exit;
                }

                import Checker;

                def CheckA(Checker) {
                    OK->CheckAOK;
                    NG->CheckANG;
                    Pos:(400, 100);
                    Color: "orange";
                }

                def CheckB(Checker) {
                    OK->CheckBOK;
                    NG->CheckBNG;
                    Pos:(400, 350);
                    Color: "teal";
                }

                def Start(Start)
                {
                    1->StartEvent;
                    Pos:(100, 200);
                    Color: "red";
                    Type: Start;
                    FlowID: 1;
                }
                StartEvent->Start to CheckA.Entry;

                def End(End)
                {
                    1->EndEvent;
                    Pos:(700, 200);
                    Color: "indigo";
                    Type: End;
                    FlowID: 2;
                }

                def ErrorEnd(End)
                {
                    1->ErrorEndEvent;
                    Pos:(700, 450);
                    Color: "red";
                    Type: End;
                    FlowID: 3;
                }

                CheckANG->CheckA to CheckB.Entry;
                CheckAOK->CheckA to ErrorEnd;
                CheckBOK->CheckB to End;
                CheckBNG->CheckB to ErrorEnd;
                """;

            try
            {
                var engine = new FSMEngine(factory);
                engine.CreateStateMachine(script);

                Console.WriteLine("  关键连线:");
                foreach (var source in new[] { "Start", "CheckA.Entry", "CheckA.Exit", "CheckB.Entry", "CheckB.Exit" })
                {
                    foreach (var transition in engine[source].GetFSMTransitions())
                        Console.WriteLine($"    {transition.Source.Name} --{transition.Trigger.EventName}--> {transition.Target.Name}");
                }

                if (!engine.TryGetNode("Start", out IFSMNode startNode))
                    throw new Exception("Start 节点不存在");
                if (!engine.TryGetEvent("EndEvent", out FSMEvent endEvent))
                    throw new Exception("EndEvent 事件不存在");
                if (!engine.TryGetEvent("ErrorEndEvent", out FSMEvent errorEndEvent))
                    throw new Exception("ErrorEndEvent 事件不存在");

                var visited = new List<string>();
                var executor = new FSMExecutor(startNode, new[] { endEvent, errorEndEvent });
                executor.NodeStateChanged += (_, nodeName) => visited.Add(nodeName);

                await executor.RestartAsync(true);
                await Task.WhenAny(executor.ExecutorTask, Task.Delay(12000));

                var stateBeforeStop = executor.State;
                if (stateBeforeStop != FSMState.Finished)
                {
                    await executor.StopAsync();
                    throw new Exception($"执行没有完成，当前状态: {stateBeforeStop}, visited=[{string.Join(" -> ", visited)}]");
                }

                if (!visited.Contains("End") && !visited.Contains("ErrorEnd"))
                    throw new Exception($"执行没有进入任何顶层 End 节点，visited=[{string.Join(" -> ", visited)}]");

                Console.WriteLine("  [OK] 用户脚本能执行到顶层 End 节点并结束");
                Console.WriteLine("  Test 8 通过\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Test 8 失败: {ex.Message}\n");
                throw;
            }
        }

        // ================================================================
        // Test 9: 复现 UI 拖线 bug — terminal 列表里只有 1 个节点时不发布目标 event，
        //        fallback 错误地把 transition 挂到该 terminal 上，并改写它的 branch。
        // ================================================================
        static async Task Test9_TerminalFallbackBug(ReflectionNodeFactory factory)
        {
            Console.WriteLine("--- Test 9: terminal fallback bug 复现 ---");

            // 模板关键：terminal 只声明 Exit，没有 Entry。
            // Entry 才是发布 NG 的节点，Exit 只发布 OK。
            var script = """
                module Checker {
                    input Entry;
                    output OK, NG;
                    terminal Exit;

                    def Entry(TestIdle)
                    {
                        1->NextEvent;
                        3->NG;
                        Pos:(100, 200);
                        Color: "light-blue";
                    }

                    def Exit(TestEnd)
                    {
                        1->OK;
                        Pos:(450, 255);
                        Color: "green";
                    }

                    def NextEvent as event;
                    def NG as event;
                    def OK as event;
                    NextEvent->Entry to Exit;
                }

                import Checker;

                def CheckA(Checker) {
                    OK->CheckAOK;
                    NG->CheckANG;
                    Pos:(400, 100);
                    Color: "orange";
                }

                def CheckB(Checker) {
                    OK->CheckBOK;
                    NG->CheckBNG;
                    Pos:(400, 350);
                    Color: "teal";
                }

                def Start(Start)
                {
                    1->StartEvent;
                    Pos:(100, 200);
                    Color: "red";
                    Type: Start;
                    FlowID: 1;
                }
                StartEvent->Start to CheckA.Entry;

                def End(End)
                {
                    1->EndEvent;
                    Pos:(700, 200);
                    Color: "indigo";
                    Type: End;
                    FlowID: 2;
                }

                def ErrorEnd(End)
                {
                    1->ErrorEndEvent;
                    Pos:(700, 450);
                    Color: "red";
                    Type: End;
                    FlowID: 3;
                }

                CheckANG->CheckA to CheckB.Entry;
                CheckAOK->CheckA to ErrorEnd;
                CheckBOK->CheckB to End;
                CheckBNG->CheckB to ErrorEnd;
                """;

            try
            {
                var engine = new FSMEngine(factory);
                engine.CreateStateMachine(script);

                Console.WriteLine("  关键连线:");
                foreach (var source in new[] { "Start", "CheckA.Entry", "CheckA.Exit", "CheckB.Entry", "CheckB.Exit" })
                {
                    foreach (var transition in engine[source].GetFSMTransitions())
                        Console.WriteLine($"    {transition.Source.Name} --{transition.Trigger.EventName}--> {transition.Target.Name}");
                }

                // 关键断言：CheckANG 必须从 CheckA.Entry 出发（因为 Entry 才发布 NG）
                var ngTransition = engine["CheckA.Entry"].GetFSMTransitions()
                    .SingleOrDefault(t => t.Trigger.EventName == "CheckANG");
                if (ngTransition == null)
                    throw new Exception($"CheckANG 应该从 CheckA.Entry 出发，但 Entry 上没有这条 transition");
                if (ngTransition.Target.Name != "CheckB.Entry")
                    throw new Exception($"CheckANG 应该连到 CheckB.Entry，实际连到 {ngTransition.Target.Name}");

                // 同时确保 CheckA.Exit 没被错误改写
                var exitBranches = engine["CheckA.Exit"].EventDescriptions ?? new();
                if (exitBranches.Any(d => string.Equals(d.Description, "CheckANG", StringComparison.OrdinalIgnoreCase)))
                    throw new Exception("CheckA.Exit 的 branch 被错误改写为 CheckANG");

                Console.WriteLine("  [OK] CheckANG 正确从 CheckA.Entry 出发");
                Console.WriteLine("  [OK] CheckA.Exit 的 branch 没被改写");
                Console.WriteLine("  Test 9 通过\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Test 9 失败: {ex.Message}\n");
                throw;
            }
        }

        // ================================================================
        // Test 10: terminal 不发布目标事件时，脚本路径应报错而非静默 fallback
        //         复现审计报告 ①：单 terminal fallback 会凭空把错误事件名塞进 terminal branch
        // ================================================================
        static void Test10_TerminalFallbackRejectedScript(ReflectionNodeFactory factory)
        {
            Console.WriteLine("--- Test 10: terminal fallback 应报错（脚本路径） ---");

            // module 有 1 个 terminal Exit（只发布 OK）。
            // 外部连线用 WrongEvent：既不是 output 映射值，Exit 也不发布它。
            // 当前 bug：fallback 静默把 WrongEvent 塞进 Exit 的 branch。
            // 期望：抛 ScriptException。
            var script = """
                module M {
                    input Entry;
                    output OK;
                    terminal Exit;

                    def Entry(TestIdle)
                    {
                        1->Next;
                        Pos:(100, 100);
                    }

                    def Exit(TestEnd)
                    {
                        1->OK;
                        Pos:(300, 100);
                    }

                    def Next as event;
                    def OK as event;
                    Next->Entry to Exit;
                }

                import M;

                def Sub1(M)
                {
                    OK->OKOut;
                    Pos:(0, 0);
                }

                def Start(TestStart) { 1->StartEvt; }
                def Target(TestEnd) { 1->DoneEvt; }

                StartEvt->Start to Sub1.Entry;
                WrongEvent->Sub1 to Target;
                OKOut->Sub1 to Target;
            """;

            try
            {
                var engine = new FSMEngine(factory);
                engine.CreateStateMachine(script);
                Console.WriteLine("  Test 10 失败: 应该抛出异常但未抛出（fallback 静默篡改了 terminal）");
            }
            catch (ScriptException ex)
            {
                Console.WriteLine($"  [OK] Test 10: 正确拒绝（{ex.Message}）");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Test 10 失败: 抛了非 ScriptException: {ex.GetType().Name}: {ex.Message}");
            }

            Console.WriteLine("  Test 10 完成\n");
        }

        // ================================================================
        // Test 11: terminal 不发布目标事件时，Fluent API 路径也应报错
        //         审计报告 ① 在 FSMDefineBuilder + FSMTransformBuilder 的镜像
        // ================================================================
        static void Test11_TerminalFallbackRejectedFluent(ReflectionNodeFactory factory)
        {
            Console.WriteLine("--- Test 11: terminal fallback 应报错（Fluent API 路径） ---");

            var moduleScript = """
                module M {
                    input Entry;
                    output OK;
                    terminal Exit;

                    def Entry(TestIdle)
                    {
                        1->Next;
                        Pos:(100, 100);
                    }

                    def Exit(TestEnd)
                    {
                        1->OK;
                        Pos:(300, 100);
                    }

                    def Next as event;
                    def OK as event;
                    Next->Entry to Exit;
                }
            """;

            void RunCase(string label, Action<IFSMBuilderStepConstruct> configure)
            {
                try
                {
                    var engine = FSMEngineBuilder.Create()
                        .ConfigureNodeFactory(factory)
                        .ConfigureModule("M", moduleScript);
                    configure(engine);
                    Console.WriteLine($"  Test 11 失败 [{label}]: 应该抛出异常但未抛出");
                }
                catch (ScriptException ex)
                {
                    Console.WriteLine($"  [OK] Test 11 [{label}]: 正确拒绝（{ex.Message}）");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Test 11 失败 [{label}]: 抛了非 ScriptException: {ex.GetType().Name}: {ex.Message}");
                }
            }

            // FSMDefineBuilder 路径
            RunCase("DefineBuilder", e => e.ConfigureFSMDefine(def => def
                .AddNode<TestStartNode>("Start")
                .AddNode<TestEndNode>("Target")
                .AddModule("M", "Sub1", m => m.MapOutput("OK", "OKOut").SetPosition(0, 0))
                .AddConnection("StartEvt", "Start", "Sub1.Entry")
                .AddConnection("WrongEvent", "Sub1", "Target")
                .AddConnection("OKOut", "Sub1", "Target"))
                .Build());

            // FSMTransformBuilder 路径
            RunCase("TransformBuilder", e => e
                .ConfigureFSMDefine(def => def
                    .AddNode<TestStartNode>("Start")
                    .AddNode<TestEndNode>("Target")
                    .AddModule("M", "Sub1", m => m.MapOutput("OK", "OKOut").SetPosition(0, 0))
                    .AddConnection("StartEvt", "Start", "Sub1.Entry")
                    .AddConnection("OKOut", "Sub1", "Target"))
                .TransformFSMDefine(t => t.AddTransform("WrongEvent", "Sub1", "Target"))
                .Build());

            Console.WriteLine("  Test 11 完成\n");
        }

        // ================================================================
        // Test 12: output 映射值与 module 内部事件全名碰撞时应报错
        //         审计报告 ②：实例名.Next 这种内部事件全名被用作 output 映射值时，
        //         内外会合并成同一个 FSMEvent，产生跨边界联动。
        // ================================================================
        static void Test12_OutputMapEventNameCollisionRejected(ReflectionNodeFactory factory)
        {
            Console.WriteLine("--- Test 12: output map 值撞内部事件名应报错 ---");

            // module M 内部节点 Work 发布 Next（展开后全名 Sub1.Next）。
            // 实例 Sub1 把 output Out 映射到 "Sub1.Next" —— 恰好等于内部事件全名。
            // 当前 bug：静默合并，构建成功。
            // 期望：抛 ScriptException。
            var moduleScript = """
                module M {
                    input Entry;
                    output Out;

                    def Entry(TestIdle)
                    {
                        1->Next;
                        Pos:(100, 100);
                    }

                    def Work(TestIdle)
                    {
                        1->Next;
                        Pos:(300, 100);
                    }

                    def Next as event;
                    Next->Entry to Work;
                }
            """;

            var script = moduleScript + """

                import M;

                def Sub1(M)
                {
                    Out->Sub1.Next;
                    Pos:(0, 0);
                }

                def Start(TestStart) { 1->StartEvt; }
                def Target(TestEnd) { 1->DoneEvt; }

                StartEvt->Start to Sub1.Entry;
            """;

            void RunCase(string label, Action run)
            {
                try { run(); Console.WriteLine($"  Test 12 失败 [{label}]: 应该抛出异常但未抛出"); }
                catch (ScriptException ex) { Console.WriteLine($"  [OK] Test 12 [{label}]: 正确拒绝（{ex.Message}）"); }
                catch (Exception ex) { Console.WriteLine($"  Test 12 失败 [{label}]: 抛了非 ScriptException: {ex.GetType().Name}: {ex.Message}"); }
            }

            // 脚本路径
            RunCase("Script", () =>
            {
                var engine = new FSMEngine(factory);
                engine.CreateStateMachine(script);
            });

            // Fluent API 路径
            RunCase("Fluent", () =>
            {
                var engine = FSMEngineBuilder.Create()
                    .ConfigureNodeFactory(factory)
                    .ConfigureModule("M", moduleScript)
                    .ConfigureFSMDefine(def => def
                        .AddNode<TestStartNode>("Start")
                        .AddNode<TestEndNode>("Target")
                        .AddModule("M", "Sub1", m => m.MapOutput("Out", "Sub1.Next").SetPosition(0, 0))
                        .AddConnection("StartEvt", "Start", "Sub1.Entry"))
                    .Build();
            });

            Console.WriteLine("  Test 12 完成\n");
        }
    }

    internal sealed class ReflectionNodeFactory : IFSMNodeFactory
    {
        private readonly Dictionary<string, Type> keyToType;
        private readonly Dictionary<Type, string> typeToKey;

        public ReflectionNodeFactory(IEnumerable<Assembly> assemblies)
        {
            keyToType = new();
            typeToKey = new();

            foreach (var asm in assemblies)
            {
                foreach (var t in asm.GetTypes().Where(t => typeof(IFSMNode).IsAssignableFrom(t) && !t.IsAbstract))
                {
                    var attr = t.GetCustomAttributes(typeof(FSMNodeAttribute), false).FirstOrDefault() as FSMNodeAttribute;
                    if (attr == null || string.IsNullOrWhiteSpace(attr.Key)) continue;
                    if (!keyToType.ContainsKey(attr.Key))
                    {
                        keyToType[attr.Key] = t;
                        typeToKey[t] = attr.Key;
                    }
                }
            }
        }

        public IFSMNode CreateNode(string name)
        {
            if (!keyToType.TryGetValue(name, out var t))
                throw new InvalidOperationException($"Node key '{name}' 未注册");
            return (IFSMNode)Activator.CreateInstance(t)!;
        }

        public Type GetNodeType(string name)
        {
            if (!keyToType.TryGetValue(name, out var t))
                throw new InvalidOperationException($"Node key '{name}' 未注册");
            return t;
        }

        public string GetNodeFeatureName(Type type)
        {
            if (typeToKey.TryGetValue(type, out var key)) return key;
            var attr = type.GetCustomAttributes(typeof(FSMNodeAttribute), false).FirstOrDefault() as FSMNodeAttribute;
            if (attr != null && !string.IsNullOrWhiteSpace(attr.Key)) return attr.Key;
            throw new InvalidOperationException($"类型 {type.FullName} 未标记 FSMNodeAttribute");
        }
    }
}
