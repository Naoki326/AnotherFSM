# #33 完整 DSL 导航与跨文件引用

状态：实现、回归、独立双轴审查及最终提交 ZIP 的真实宿主复验通过。远端交付状态以 [Issue #33](https://github.com/Naoki326/AnotherFSM/issues/33) 和收尾回读为准。

基线：`1ef8913`。仅实现本票，不修改已关闭父规格/地图，不认领 #34–#36。

## 边界与测试 seam

用户确认三层测试 seam：共享核心公开导航/引用及版本接口、Rider 项目发现/原生搜索 SDK 接口、真实 Rider 正式动作与结果生命周期。

本票覆盖全名称、Module/实例/接口、实际 import 图、全部项目项脚本入口、解决方案脚本引用和当前项目 C# 关联。跨项目类型可见性、多宿主/TFM、DLL/NuGet、可选配置属于 #34；完整补全、悬停及 Quick Documentation 属于 #35；最终性能预算和全量支持矩阵属于 #36，均不提前宣称完成。

权威规则：[#26 符号身份](https://github.com/Naoki326/AnotherFSM/issues/26#issuecomment-6673564543)、[#27 交互](https://github.com/Naoki326/AnotherFSM/issues/27#issuecomment-6673911637)、[#28 技术路线](https://github.com/Naoki326/AnotherFSM/issues/28#issuecomment-6683161320)、[#29 支持矩阵](https://github.com/Naoki326/AnotherFSM/issues/29#issuecomment-6684053764)。默认特性关联按零配置修订理解，不证明运行时实际注册。

## 实现入口

- `AnotherFSM.Semantics/SemanticGraph.cs`：独立语义查询，源码/实例身份分层、公开接口、真实 Branch 发布者、精确导入关系；不调用运行时建图。
- `shared/DeclarationRules.cs`：严格运行时与编辑核心共用注册名、Branch 数字、公开限定接口及 Namespace 优先规则，既有生成器继续使用原有声明规则，不全面重写生成器。
- `rider/backend/ScriptDocuments.cs`：根来自宿主项目项；依赖来自精确 import。项目 fallback 绑定真实 IProjectFile 文档，不能把路径版磁盘文档冒充未保存项目缓冲区。解析/图/来源缓存有界、随失效释放。
- `rider/backend/BridgeQueries.cs`：异步带代次查询、跨文件源码快照、精确位置/文本/实例身份重验；保持“位置集合”和“唯一声明”的语义区别，即使集合只有一行。
- `rider/backend/NativeSearch.cs`：原生 C# Find Usages 只贡献确定的真实工厂键范围。每次文件/元素回调遵守本身的域，不按整个模块提交其他文件。
- `rider/frontend/src/anotherfsm/SemanticUi.java`：只呈现共享结果，不复制名称/绑定语义；多目标 popup、独立引用页、主动过期、显式刷新和点击重验。

## 可观察操作

右键提供：转到声明、转到 C# 实现、查找引用、查找模板全部引用、转到模块定义、转到入口、查看内部定义／真实发布位置、查看分支元数据。

单目标定位真实源码，多目标由 popup 选择。引用页每次具有独立 Content；变更后不重排旧行。过期行拒绝导航，当前行仍需后端确认，再比较目标编辑缓冲区。无目录的非物理编辑器保留原 VirtualFile；无宿主不借用相邻项目绑定。

## 验收台账

以下对应实现提交 `bf22f95` 的实测结果。原始报告、安装回读及截图保存在 [rider/evidence/33](../../rider/evidence/33/)；文档/证据后提交，不把其 Git 身份冒充 ZIP 的源码身份，也不用下表代替远端回读。

| AC 组 | 已核实结果 |
| --- | --- |
| 共享规则与回归 | 核心 63 项、严格 runtime 310 项通过；真实生成器消费者编译、访问器及 Module 流程执行通过；插件分析没有运行这些执行入口 |
| 全名称 | 真实动作验证 Node/Event/Namespace/Branch/Module/import 别名与目标/实例/Input/Output/限定名；隐式 Event 展示引入/使用集合 |
| 模板/实例身份 | 模板 Output 7 个真实位置，Left Output 5 个、Left Input 4 个、Input 模板 5 个；实例不混入其他实例，未打开的第三实例被包含；整实例也能切到 Module 模板引用 |
| 公开连接与落点 | 单/多 Input、损坏 Input 不减声明个数、私有拒绝、别名链的本地落点/真实模块目标；Output 两个真实发布 Node，同 Node 多 Branch 去重，覆盖后的 Branch 不充当发布者；普通点号名称不冒充实例 |
| Branch | error=3、break=4、cancel=5；同所属 Node/同索引查询；明确 Indexes 元数据定位实际特性，无元数据说明而非跳枚举或 return/yield |
| 项目项 | AdditionalFiles、None、Content、实体 EmbeddedResource、linked 和两扩展名实际发现、导航及 C# 关联；目录内未归属脚本不获得项目身份 |
| import | 实体目录、传递依赖、别名/同名 decoy、缺失/循环/破损及可靠局部恢复；Link 展示目录不参与解析，不新增路径语法 |
| 查询生命周期 | 独立同标题页、未保存依赖/模板/import 编辑、主动过期、稳定旧行、拒绝旧行 Enter/双击、选择性刷新、真实源位置点击；#32 原生页身份及旧导航守卫回归通过 |
| C# 关联 | Module 内 Node/Input 导航；所有项目项及仅经 import 引入的非项目项模板，原生反查 49 个真实工厂键范围，含 import-only 的 `83/8 GraphKey`，按源码位置去重（原生 UI 可另按行合并显示）；显式单文件受限查询不泄漏其他来源 |
| 独立/编辑文本 | loose 与无目录非物理文件的局部辅助；宿主/相对目录不足说明；无关不完整尾部不抹除可信接口；半截头部不产生确定关联 |
| 宿主与安全证据 | Windows 11 x64 / Rider 2026.2.3.1，build 262.10968.170；原 #32 旅程 51 阶段、新旅程 117 项、SDK 查询/来源检查及真实 socket 断开/重连通过；无新增业务执行见下节边界 |

## 关键 SDK 事实和红绿修复

1. 旧 #32 ZIP 原旅程通过，但 import 导航明确失败，保留真实 red。后续修复 Module 引用误计声明、遗漏单入口省略引用、专用“入口”操作误退回模块头部等，核心有独立红绿用例。
2. 路径版与 IProjectFile 版 DocumentManager 文档不等价；用真实项目文档做未保存分析及最后校验。Content 修改为另一有效键但不保存时，正式动作定位 BareWorker，磁盘仍为原文。
3. popup 接受时先触发关闭，再调用选中回调；使用 SDK 的 `isOk()` 区分接受/取消，不能提前取消用户刚接受的导航。
4. 原生宽域先按原 solution 域做词索引 narrowing，合并的 whole-module 域又会吞掉同模块显式 source，visitor 仅枚举 `module.SourceFiles`。因此简单把 import-only source 加到声明域并不足够。真实 SDK 已证明语言/单源词过滤均可用、显式 source 查询命中，宽域却遗漏。
5. 使用公开 `ITwoPhaseDomainSpecificSearcher` 的第二阶段，把**精确 import-only source 域与原始用户域相交**后交回宿主 visitor。没有伪造项目项、关闭全局词过滤或恢复域外提交。SDK 与原生 UI 均包含 LinkedChecker.fsm 的真实键；显式 file A 查询仍只有 A 的两个键，重复回调不永久吞结果。
6. 审查修复整实例模板切换、损坏多 Input 不得降成单入口、别名链归一目标；SDK 设计复核进一步确保 `processedFiles` 不冒充完整处理，只记录本查询成功完成的整文件回调，节点访问不抑制整个文件，保持取消及 epoch 边界。
7. 测试修正同样留痕：正例不含非法嵌套 use/重复 source+Event；token 定位不选 LaunchEntry 的子串；跨文件未保存编辑先经真实编辑器绑定；行的真实来源与“实例上下文”文字分开判断。引用数断言未降级。Enter 测试调用公开 Swing binding，不向其他 OS 前台应用注入全局按键。

## 证据分层与边界

- 全 .NET solution **498 passed**（核心 63 / runtime 310 / API 83 / 共享浏览器会话 42）；React 12、仓库工具 22、发布工具 6；typecheck/lint 通过。
- 目标 SDK Debug/Release 与开发 ZIP 构建通过。SDK 查询/发现检查 **81 项**，另有 Branch 元数据及单行位置集合检查；这些不冒充 UI 验收。
- 最终提交宿主原始目录：`.scratch/issue-33/host/final03-d6452f6259a547f7a0ddb924b5aff2a1/acceptance/`。原 #32、新 117 项、原生 C# 反查、未保存文本和 socket 恢复分别报告，原生查询同时确认真实 `isSearchComplete`。安装哈希及来源见 [installation-readback.json](../../rider/evidence/33/installation-readback.json)。
- `bf22f95` 连续两次构建 ZIP 逐字节一致，实际安装 SHA-256：`e01e22d8a95b7c8af8417aae131436a4324bea2843bc71443af933c02e66260a`。本机产物：`rider/build/tools-build/anotherfsm.zip`；重现此哈希时使用该实现提交及锁定工具链。
- 项目夹具的 factory、ctor、getter、执行入口哨兵未触发；缺省 FSMNode 特性仅读 PSI 静态元数据。静态调用审计无插件新增业务注册/建图/业务程序集加载、构建/restore/生成器、下载或凭据入口。哨兵缺失只证明被仪器化入口，不能当成所有安全边界的万能证明。
- 受控夹具和生成器回归的显式构建/执行，与插件分析业务项目严格区分。宿主已有受信任加载及自身网络行为不冒称插件新增行为。
- 未测最终 P95/内存预算，不把原型、核心、编译或测试总数等同于完整首版矩阵；不追加用户手测待办。

## 收尾门槛

双轴审查的初轮发现及逐条处置见 [review.md](../../rider/evidence/33/review.md)。最终增量复核确认 Standards / Spec 未解决发现均为 0。正常 push、工单证据/关闭及远端提交回读是交付完成门槛，实际状态由工单最终评论和关闭状态记录；本地提交不冒充远端交付。
