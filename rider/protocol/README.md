# #32：最小 RD Solution 扩展

仅提供协议生成、双端公共 SDK 编译 seam 和获取/绑定示例，**不是完整插件，也不代表 #32 全票 AC 通过**。

## 契约与集成交接（已生成并编译）

- `model/AnotherFsmModel.kt:11–18`：`Ext(SolutionModel.Solution)`；异步 `call<string,string> query`、异步双向 `signal<string> invalidated`。
- 前端 `generated/kotlin/AnotherFsmModel.Generated.kt:43–44`：`IRdCall<String,String>` / `IAsyncSignal<String>`；最后一行生成 `Solution.anotherFsmModel`。
- 后端 `generated/csharp/AnotherFsmModel.Generated.cs:45–46`：`IRdEndpoint<string,string>` / `ISignal<string>`；`:115–118` 生成 `Solution.GetAnotherFsmModel()`。
- 两端 extension key 均由 rd-gen 生成为 `anotherFsmModel`，serialization hash 均为 `-85057710393708128`。没有手写 key→消息映射，也没有创建 protocol、wire 或 socket。

编译过的真实宿主入口在 `examples/`：

```kotlin
// import com.jetbrains.rider.projectView.solution
// import com.anotherfsm.rider.protocol.anotherFsmModel
val model = project.solution.anotherFsmModel
val task = model.query.start(requestLifetime, jsonEnvelope)
model.invalidated.advise(solutionLifetime) { /* 由适配层处理失效 */ }
```

```csharp
// using JetBrains.ReSharper.Feature.Services.Protocol;
// using AnotherFSM.Rider.Protocol;
// using JetBrains.Rd.Tasks;
var model = solution.GetProtocolSolution().GetAnotherFsmModel();
model.Query.SetAsync(handler); // Func<Lifetime, string, Task<string>>
model.Invalidated.Fire(jsonEnvelope);
```

**调用前提**：已有宿主 Solution，在宿主协议 scheduler 上首次获取/安装扩展；`RdExtBase.extThreading` 仍为 Default，成员 `.async` 并不改变扩展首次绑定的线程要求。前端异步取得任务，不调用 `sync` 或 UI 阻塞等待。后端 handler 收到 RD 每次请求的 Lifetime，不执行任何业务构造器/节点/工厂。

主代理集成时必须在 Solution 可用后安装一次 handler（避免首个请求先于后端安装），在关闭/重连后重新获取模型，不跨 Solution 保留实例；订阅与请求 lifetime 由宿主管理。JSON 的 `docVersion/contextVersion/session/requestId`、取消传播到核心、旧结果丢弃、PSI 读锁、线程调度及错误 envelope 都由后续适配层实现；当前仅透明传输字符串。信号为双向契约，当前示例仅展示后端发出。

生产插件分别纳入 `generated/csharp/*.cs` 和 `generated/kotlin/*.kt`；示例不是自动注册的 service/component，`Protocol.Compile.csproj` 仅为编译探针，不是插件打包工程。不应把生成期 `rd-gen.jar` 或 `rider-model.jar` 打进运行时插件。

## 可重复生成/编译

本次 Windows 11 环境：

- 安装：`C:/Users/Naoki/AppData/Local/Programs/Rider`，`product-info.json` 为 Rider **2026.2.3.1 / RD-262.10968.170**。
- 使用该安装的 `jbr/bin/java.exe`、`lib/rd-gen.jar`（主类 `com.jetbrains.rd.generator.nova.MainKt`）、JVM RD runtime 与 `lib/ReSharperHost` DLL；.NET SDK 10.0.300。
- 安装不含生成期 `lib/rd/rider-model.jar`，也未发现 Kotlin compiler。生成期模型来自官方 [SDK ZIP](https://www.jetbrains.com/intellij-repository/releases/com/jetbrains/intellij/rider/riderRD/2026.2.3.1/riderRD-2026.2.3.1.zip) 的 `lib/rd/rider-model.jar`，SHA-256：`5d9fe436a0ec675e9ad7e300a054b7b51fb66b00b3fff2b182c2893539b6d2dc`。
- **版本限制**：这个官方 SDK ZIP 的 `build.txt` 是 **RD-262.10968.171**，不是本机 `.170`。没有隐藏或宣称完全匹配；生成结果及宿主入口均对本机 `.170` 编译成功，但未证明所有生成期/运行期兼容性或真实 Rider 加载成功。
- Kotlin compiler 取自 Maven Central 的 JetBrains 官方发布：`org.jetbrains.kotlin:kotlin-compiler-embeddable:2.3.0`，依赖 stdlib/script-runtime/daemon-embeddable `2.3.0`、reflect `1.6.10`、`org.jetbrains.kotlinx:kotlinx-coroutines-core-jvm:1.8.0`，另加 `org.jetbrains:annotations:13.0`。下载 URL/文件 SHA-256 在 `rd-compiler-fetch.log`。这些不是业务项目依赖。

将上述 JAR 放入本地目录（本会话 `.scratch/issue-32/cache/`）。生成器只消费明确指定的本地依赖，**不联网/安装、不运行用户项目**；不使用 Gradle 或 NuGet SDK 包替换本机 API。

从仓库根目录在 Git Bash 执行（外层命令 timeout 120 秒；脚本每个子命令 timeout 90 秒）：

```bash
python rider/tools/generate-protocol.py \
  --rider-home 'C:/Users/Naoki/AppData/Local/Programs/Rider' \
  --rider-model .scratch/issue-32/cache/rider-model.jar \
  --kotlin-compiler-dir .scratch/issue-32/cache \
  --check --verify > .scratch/issue-32/rd-check-compile.log 2>&1
```

去掉 `--check` 才更新生成代码；`--verify` 编译双端及 `examples/`。中间文件在 `--work-dir` 下的临时目录，结束自动清理；默认 `.scratch/issue-32/`。`--check` 比较完整生成内容，不改生成代码；不依赖生成文件中的绝对路径/源码行号。`Protocol.Compile.csproj` 通过 RiderHome 引用安装的 JetBrains DLL，调用示例只用 public 成员；宽引用集合用于 SDK 编译探针，不是运行时依赖清单。

## 给主代理 build.py 的 JVM/JAR 交接

仅编译已签入的生成代码时，**不需要生成期 rider-model.jar，也不需要重新生成**：

```bash
python rider/tools/generate-protocol.py \
  --rider-home 'C:/Users/Naoki/AppData/Local/Programs/Rider' \
  --kotlin-compiler-dir .scratch/issue-32/cache \
  --compile-jvm-only \
  --jvm-jar .scratch/issue-32/anotherfsm-protocol.jar \
  > .scratch/issue-32/rd-jvm-jar.log 2>&1
```

该命令已实际执行成功，外层 timeout 60 秒。`--jvm-jar` 输出文件可由 build.py 指定；正常生成模式也可同时加此选项。JAR 仅含 `com/anotherfsm/rider/protocol/` 的四个 JVM class 和 Kotlin module metadata，class major **61 / Java 17**，不打包 stdlib、RD 或 SDK。将此 JAR 纳入插件 `lib/`，运行时复用宿主 Kotlin/RD，不需要 compiler 或 rider-model。

精确编译等价命令见 `rd-jvm-jar.log:1`：以 compiler-dir JAR 为 Java launcher classpath，运行 `org.jetbrains.kotlin.cli.jvm.K2JVMCompiler -no-stdlib -no-reflect -jvm-target 17 -module-name anotherfsm_protocol -classpath <runtime-cp> -d <output.jar> <generated.kt>`。`<runtime-cp>` 仅由本机 `lib/` 中以下五个文件组成，分隔符 Windows 为 `;`：

- `util-8.jar`（宿主 Kotlin stdlib）
- `annotations.jar`
- `intellij.libraries.rd.core.jar`
- `intellij.libraries.rd.framework.jar`
- `intellij.rd.ide.model.generated.jar`（宿主 Solution；不是生成期 rider-model）

Java 端也已通过真实 `javac.exe --release 17` 检查，参见 `examples/FrontendJavaBinding.java:14–19`：

```java
AnotherFsmModel model = AnotherFsmModel_GeneratedKt.getAnotherFsmModel(
    SolutionHostExtensionsKt.getSolution(project));
IRdTask<String> task = model.getQuery().start(requestLifetime, jsonEnvelope, null);
```

这里的静态 getter **确实是 `AnotherFsmModel_GeneratedKt`**（不是按文件名猜测），JAR 目录和 javac 均已确认。编译前端 Java 时 classpath 加上述协议 JAR 与现有 Rider SDK；`null` 为 RD 可选 responseScheduler，仍须由集成层安排结果/UI调度。`--verify` 另外编译 Kotlin 和 Java 获取方式与 C# 后端绑定，不运行它们。

## 已核实证据

以下日志均位于 `.scratch/issue-32/`（本地证据，不宣称版本化或远端可访问）。命令可从日志 `+ ...` 行回读：

| 证据 | 命令 / 关键输出 |
| --- | --- |
| `rd-baseline.log` | `git rev-parse HEAD` → `f19aa76e54724743a587a44bf3ac9e7f51f58d32`；初始状态干净。 |
| `rd-sdk-urls.log`、`rd-sdk-urls-2.log` | Python urllib HEAD：build-number URL 404；产品版本 SDK ZIP 200。本机 product-info 明确 `.170`。 |
| `rd-sdk-extract.log` | HTTP Range 读取官方 ZIP，只提取模型和 build 文本；打印模型 hash 及 `b'RD-262.10968.171'`，未下载整个约 4 GB ZIP。 |
| `rd-generate-2.log` | 实际安装 rd-gen：`singleton AnotherFsmModel :> concrete_class Solution :> singleton SolutionModel :> root IdeRoot`；Kotlin AsIs / CSharp Reversed；生成成功。 |
| `rd-check-compile.log:35–53` | 同一生成器 `--check --verify` → 两个 `MATCH`；`PASS Kotlin generated model + host Project binding`；C# build **0 warnings / 0 errors**；`PASS C# generated model + host ISolution binding`。不是 stub SDK 编译。 |
| `rd-jvm-jar.log`、`rd-check-compile-final.log` | 前者输出 `JVM_JAR` 和四个 major 61 class；后者实际 `javac.exe --release 17` → `PASS Java consumption + host Project binding`，并再次通过双端生成比对及编译。 |
| `rd-host-api.log:1–30` | `ilspycmd.exe -t JetBrains.ReSharper.Feature.Services.Protocol.ProtocolExtensions .../JetBrains.ReSharper.Feature.Services.dll` → public `GetProtocolSolution(ISolution)`；另查 public endpoint 接口。 |
| `rd-call-api.log:129–133` | `ilspycmd.exe -t JetBrains.Rd.Tasks.RdTaskEx .../JetBrains.RdFramework.dll` → `[PublicAPI] public static void SetAsync<TReq,TRes>(IRdEndpoint<...>, Func<Lifetime,TReq,Task<TRes>>, ...)`。 |
| `rd-binding-api.log:1–18`、`rd-public-api-check.log:1–4` | JVM 反射及 classfile 注解读取：`SolutionHostExtensionsKt.getSolution(Project)` 为 public static final (`0x19`)，方法仅有 NotNull，类未标 ApiStatus.Internal；随后实际 Kotlin 编译验证 import 和调用。静态公开性不代表长期稳定。 |
| `rd-provenance.log` | Java 版本、目标 JAR/DLL hash、交接源文件逐行编号。 |

早期精确失败也保留，没有将失败当成功：`java -jar rd-gen.jar` 无 Main-Class（改用主类）；Kotlin compiler 不展开 classpath `*`（已改显式 JAR）；探测 javap 不存在 / jdk.jdeps module 不可用（改用反射和 classfile 注解检查）；错误 namespace 的 C# 探针失败后按目标 SDK 的真实 namespace 重查成功。

第一方依据：RD [生成文档](https://github.com/JetBrains/rd/blob/a35c1e8642656560c9f7358638feae9343d58c61/docs/rd-gen.md)、[GenerationSpec 格式源码](https://github.com/JetBrains/rd/blob/a35c1e8642656560c9f7358638feae9343d58c61/rd-kt/rd-gen/src/main/kotlin/com/jetbrains/rd/generator/nova/GenerationSpec.kt)、官方模板引用的 [Solution 扩展](https://github.com/ForNeVeR/rider-plugin-template/blob/f91bed6e211b9e257363e5700b385a1c845c1e65/content/protocol/src/main/kotlin/model/rider/RdPluginTemplateModel.kt) 与 SDK 模型路径。回读结果见 `rd-official-and-help.log`、`rd-spec-source.log`、`rd-upstream-build.log`、`rd-download-discovery.log`。

## 未核实 / 不在本子任务内

没有启动 Rider、注册插件、开 solution、做双端真实请求往返、跨语言 wire 序列化运行测试、取消/重连实测或 ZIP 安装。生成+编译通过仅证明这一公共 seam 可用，不能推断宿主生命周期及版本处理正确。

核心 grammar 复用、静态 FSMNode/IFSMNode 身份绑定、DSL↔C#、未保存缓冲区与旧结果、真实宿主/ZIP 仍须主代理继续完成。#33 Module/import 全名、#34 跨项目、#35 编写辅助、#36 性能不在本实现内。
