# #22 最终收缩与父规格追溯

实施基线：`63308a7486b73a3ed4cd495689e225a14498923d`；HEAD / merge-base / `implement/context-lifecycle` 三者一致，隔离 worktree 开工干净。#17–#21 已交付关闭；#22 已认领、原生依赖 #20/#21 closed（外置 `claim.json` / `dependencies.json` / `closed.json`）。本票不 push、不关票、不合主树、不委派或自行审查。

原始日志：`C:/work/anotherfsm-context-19/evidence-22/`。本文件为版本化交付入口，不以 ignored 目录保留唯一报告。

## 逐 slice 的真实红绿

1. 无依赖准备：`01-free-red.log` 为缺少 `FSMEngine.Prepare` 的真实 CS1061；首次同时含 fixture 把 void CreateNode 当返回值的 CS0030，已修正。实现无业务输入准备、全引擎需求检查与原子占用后 `01-free-green.log` 1/1，补充必要来源不得免绑定后 `01-free-contract.log` 2/2。没有空业务对象、动态来源或另一套启动算法，复用 #19/#20 的 preparation。
2. 旧入口删除：`02-removal-red.log` 真实证明旧构造仍可编译；删除公开构造、四个 Restart 和无轮次 Handle，正常宿主及生成消费者统一 Prepare。`02-entry-compiler.log` 的前八项分别实际检查 CS1729 / CS1061；随后旧 Context 尚可编译，留作下一 slice 的真实红灯。`02-core-adaptation.log` 记录迁移后的 9 个 fixture/断言适配失败，没有删除安全测试换绿；校正真实元数据、显式引擎图归属、初始化停止同步点后 `02-solution-green.log` 核心266/API76/宿主.NET36 全过。

原四个重启入口通过四个真实编译负例拒绝；原安全族迁到新公开路径：同引擎竞争、真退出前重绑拒绝/新输入、四种 Start 形状的初始化停止与异常、引用身份、私有初始化、恢复标记、旧句柄/迟到回调、旧队列及外引擎实例归属。API 的单个“Observe 后第一次 legacy Restart 前”专属场景随该入口删除，保留无依赖/类型化的 Observe→round 发布窗口与初始化取消屏障，并由编译负例证明旧入口不可复活（API 77→76）。

## 最终验收状态

实施中；最终分层命令、全部 AC 及父验收族追溯将在后续 slice 完成后记录。当前不宣称父规格完成。
