# #32 双轴审查与逐项处置

比较基线：`f19aa76`；初轮实现 `b08befa`；修复复核 `b08befa...b249346`。
两轴由独立 code-reviewer 并行执行，只读，不将审查当测试。

## Standards

初轮无文档硬违规，1 项 heuristic：possible Primitive Obsession，过期通知以展示标题代替查询身份。

**采纳并核实修复**：公开 `RiderFindResultsBrowser.Sessions` 中 request 引用唯一匹配真实 native session，跨端使用已序列化的 `ListId`；标题只用于显示。前端收到通知后回读该 ListId 当前 query epoch，不直接应用旧通知状态。未知或双向多匹配不取首项、不回退标题，不替换 provider 或另造原生模型。

复核结论：首轮问题已针对性解决；按 12 类 smell baseline 未发现新的有据硬违规或实质性启发式发现。

## Spec

初轮：
1. 重连后的真实证据 partial：在初轮文档状态下成立。后续实际关闭原宿主 loopback socket，观察原 wire true→false→true、新 socket、原 backend 保持存活、旧 handle 取消，新实际 RD 与正式导航成功。没有把“同 JVM 更换 backend PID”这一测试自加条件当成产品 AC。
2. 零 DSL 命中旧查询在移除特性后漏标：采纳。旧 ZIP 上 UnusedWorker 用例在阶段 42 失败（保留红灯报告）。修复在真实查询创建时同步捕获历史归属，Rerun 重新捕获整次代次；不再以变更后的 current IsTarget 反推过去。

复核结论：两条代码/证据路径已修复，无有据新增错误或越界；审查时仍待 main 回读的新宿主结果，现已独立核验为阶段 51 通过。

## 主代理验收

- `host-acceptance.json`：51 阶段通过，零命中移除特性、真实两页同标题不同 ListId、重播旧页失效不影响新页、Rerun 后重播旧代次不覆盖新状态。
- `host-reconnect.json`：实际 transport 断开及恢复通过。未声称人工制造了确定在途的网络乱序；文本并发替代、真实未保存操作、取消 handle 与重播旧通知分别提供各自层级的证据。
- 无 #33–#36 的条目被追加为本票缺失；没有借产品构建或原型记录替代真实宿主。

最终未解决发现：**Standards 0；Spec 0**。
