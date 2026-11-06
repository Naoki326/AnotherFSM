# #33 聚焦宿主适配核验处置

来源：只读 investigator `01a1b2d0-43d0-7598-bbcf-a4ed58468b9a`，证据为源码与目标 SDK，不是 UI 通过。

## 主代理逐条核验

1. **P1 原生搜索域泄漏：采纳。** 主代理独立反编译 `SearchProcessorBase.ProcessProjectItem` 与 `FinderSearchDomainVisitor<T>.VisitPsiSourceFile`：宿主先由域决定文件回调，再原样接收插件结果，确实不为插件跨文件提交再过滤。删除模块一次全量提交；声明域先含精确 import 来源，再由宿主与用户域相交。每次 callback 仅贡献该文件，ProcessElement 另限实际元素范围。
2. **P1 路径文档与项目文档不同：采纳。** 主代理独立核对 `DocumentToProjectFileMappingStorage.GetOrCreateDocument` 两个 overload，以及 `CanBeFilesPathBased`：普通项目不是路径表文档。项目 fallback 采用绑定真实 IProjectFile 的文本 view，显式重映射 Document/修改 stamp/已分配文档入口；最终 CurrentText 优先真实项目源文档。import-only 路径文档不据此一概判错。
3. **P2 抢占恢复可能漏模块：按推测保留，不写成已复现。** 删除永久 visitedModules 同时消除了这条提前吞掉恢复回调的路径；同一 searcher 多次同文件调用交给宿主消费者去重。SDK 回归将检查可重入性；不声称人为制造了完整宿主抢占。
4. **fallback word-index 可能不放行：待真实 SDK/宿主结果核验。** 不以 def/import/use 文本或编译成功当作 Find Usages 通过。
5. **未发现新缺陷的范围：仅采纳代码层事实。** 精确 import/项目项来源、exact位置重验和无目录本地分支仍需新 ZIP 的实际操作验证。旧 ZIP red 只证明旧实现缺功能。

修改后目标 SDK backend 编译通过，0 warning/0 error：`sdk/scope-document-check-01.log`。新增独立 SDK 受限查询域及项目文档身份回归正在准备；最终结论以真实运行产物为准。
