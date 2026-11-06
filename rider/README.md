# AnotherFSM Rider 插件（开发版，#32 / #33）

目标仅为 Windows 11 x64、Rider **2026.2.3.1 / RD-262.10968.170**。这是静态导航与引用查询的开发目录，不是完整首版支持矩阵或 Marketplace 发布。#33 的实际验收状态见对应交付记录，不能仅凭操作说明视为已通过。

## 构建

需要 .NET SDK、Python 和上述 Rider 安装。默认安装路径为 `C:/Users/Naoki/AppData/Local/Programs/Rider`，可通过 `RIDER_HOME` 或 `--rider-home` 指定。

```bash
# 显式准备插件开发工具；不属于业务项目分析流程。
python rider/tools/prepare-toolchain.py
python rider/tools/build.py
```

产物：`rider/build/tools-build/anotherfsm.zip`。打包仅包含自有双端代码、语义核心和 ANTLR runtime，不包含 Rider SDK、Kotlin compiler 或生成期模型。编译日志、命令和包内 SHA-256 清单保存在 `.scratch/issue-32/`；ZIP 中另有 `build-manifest.json`。

生成代码已签入，普通构建不重新生成协议。协议重生成、生成期 SDK `.171` 与运行期 `.170` 的已知差别及构建依赖见 [protocol/README.md](protocol/README.md)。开发工具哈希锁定在 `protocol/toolchain.json`，可用 `prepare-toolchain.py --offline` 校验已有文件。

## 隔离安装与运行

```bash
python rider/tools/sandbox.py prepare
python rider/tools/sandbox.py run
```

`prepare` 从 ZIP 安装到 `rider/.sandbox/plugins/anotherfsm`，不启动 IDE。`run` 使用独立的 config/system/plugins/log 路径，打开受控的 `rider/fixtures/Acceptance.sln`；不读取或覆盖日常 Rider 配置，不导入设置，不启用 Settings Sync。首次打开时只信任这份已知测试夹具。正常退出使用该沙盒自己的窗口。

普通项目安装后不需要 `anotherfsm.json`。插件只消费宿主已有文件、未保存缓冲区和 C# PSI，不调用运行时注册、工厂、业务 getter、特性构造器，也不启动业务构建、restore、下载或凭据流程。

## 当前操作与范围

- `.fsm` / `.FSMScript` 编辑器右键提供“转到 AnotherFSM 声明”和“转到 AnotherFSM C# 实现”。默认快捷键分别为 `Ctrl+B`、`Ctrl+Alt+B`；有 keymap 冲突时可使用右键菜单。
- 声明导航按光标所在的语义部分处理 Node、Event、Namespace、Module、import 别名/目标、Module Instance、Input/Output 与限定名称；显式工厂类型键定位确定的当前项目 C# 类型。独立的实现操作可从 Node/Input 声明或合法引用经工厂键定位 C#，不把实例或 Output 当作执行节点。
- 多个真实落点通过选择列表呈现；可另选“转到模块定义”“转到入口”“查看内部定义／真实发布位置”。“查看分支元数据”使用实际 DSL 数字索引，只在具有明确静态元数据位置时定位，不按 C# 枚举名称推断。
- “查找 AnotherFSM 引用”默认按真实符号及实例身份查询解决方案脚本；“查找模板符号全部引用”可从单实例切换到模板范围。AnotherFSM 窗口的每个页签保留独立查询，过期后只标记、不重排，使用“刷新”取得当前未保存结果。
- 根脚本仅从 Rider 已有项目项发现，覆盖 AdditionalFiles、None、Content、有实体文件的 EmbeddedResource 和 linked file。import 及传递依赖按实体文件目录查找 `M.fsm`，不使用 Link 展示目录，不猜测全解决方案同名文件。源生成器依然要求其原有 AdditionalFiles 集成。
- 从 C# 类型声明或使用处执行 Rider 原生 Find Usages，插件贡献当前项目中的工厂键引用；显式键落到键本身，隐式键落到 Node 声明并说明。
- 默认工厂键比较为 `OrdinalIgnoreCase`，Node 名保持区分大小写。真实具名 `Key` 优先于构造参数；冲突、坏值、开放泛型及不能确认的节点契约不会任取目标。
- 关联来源为 `FSMNode` 特性约定，不证明业务程序运行时实际注册。完整身份包含模块、消费 TFM 和 CLR 名，结果不缓存旧 PSI 类型对象。
- 当前缓冲区及版本参与查询，修改后旧结果失效，导航前重验；取消、项目关闭和连接变化终止相关 lifetime。

完整 DSL/Module/import 图由 #33 独立验收，记录见 [#33 交付台账](../docs/implementation/33-rider-full-navigation.md)。复杂项目与外部来源及可选覆盖（#34）、完整编写辅助（#35）、性能预算和最终安装包验收（#36）仍不提前宣称完成。缺失 import、无可靠符号或缺少 C# 宿主上下文时明确说明，不把它们伪装成成功解析。

## 验证分层

```bash
# 纯核心公共查询与版本生命周期
 dotnet test tests/AnotherFSM.Semantics.Tests --nologo
# 目标 SDK 适配编译（不是 IDE 验收）
 dotnet build rider/backend --nologo
# 构建已知夹具，安装单独的只读测试观察插件，并显式启动真实 Rider
 python rider/tools/prepare-acceptance.py --launch
```

真实宿主测试通过 `ActionManager` 调用正式插件动作和原生 Find Usages；单独的后端 observer 只观察原生 descriptor、范围、状态及事件，不执行搜索或改写结果。另一个 SDK 导航守卫探针使用真实 popup context 调用已过期 occurrence 的公开导航入口，验证拒绝跳转。它不进入开发 ZIP，只有 `--acceptance` 沙盒启用。测试会修改并恢复已知夹具的编辑缓冲区，不访问任意业务项目。功能通过后，还会关闭该测试项目当前的 loopback TCP socket，观察宿主原 wire 的真实断开/重连，再验证新的 RD 请求和正式导航；不销毁 protocol 或业务进程。进程致命故障注入是另行 opt-in 的异常安全测试，不作为默认恢复流程。

构建成功、SDK seam、核心测试、真实宿主和性能必须分别报告。#32 / #33 的实施证据与最终 verdict 分别由各自交付记录维护；未完成的宿主验收不得靠本 README 视为通过。
