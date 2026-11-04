# T1 / GitHub #3：新模块 DSL

- 工单：https://github.com/Naoki326/AnotherFSM/issues/3
- 父规格：https://github.com/Naoki326/AnotherFSM/issues/2
- 基线：`06322e367f3f032315d8975b7e1ce08dbd6bebc5`
- 边界：仅 T1 核心 DSL、引擎、公开接口解析、错误传播、测试和 ADR。T2 分析／代码生成消费者与 T3 图编辑／导出不在本票实现范围。

## 实现与验收对应

- `use Module as Instance;` 与仅含 `Pos`／`Color`／`FlowID` 的属性块；普通 `def` 始终创建真实节点，同名真实节点类型不受模块注册影响。
- 两 pass 编译期展平，模块模板和实例均不是运行时节点；实例内事件与节点具有独立限定名，无裸名全局出口、无 Branch 重绑。
- `Instance.Output -> Target;` 只接受真实实例的公开出口。公开 Input 的单入口简写、多入口选择、私有节点拒绝统一通过 `FSMEngine.ModuleInterfaces` 实现。
- 实际 Branch 出口发布者定位、同节点去重、未消费出口、多发布者、无发布者拒绝；事件身份按引擎的大小写敏感名称精确匹配，私有 `done` 不冒充公开 `Done`。
- 重复或冲突转移、内部与外部转移冲突、实际实例／节点／事件名称碰撞明确失败。`ExpandedNodeNames`／`ExpandedEventNames` 记录真实归属；同前缀但没有实际重名的普通点号名称不自动成为模块成员，普通对象大小写敏感语义保持不变。
- Namespace 与真实限定实例名共存，普通点号节点／事件仍使用事件优先转移。
- 同文件模块、已注册模块、文件注册与相对路径 import；重复／菱形依赖不误报循环，真实循环明确失败；导入别名不得覆盖已有不同模块。
- 旧模块 `def`、`terminal`、名称／数字出口映射、事件加实例占位源明确拒绝并提示迁移；词法和语法错误传播为 `ScriptException`，Try 入口返回失败。
- `Transform` 保持清除全部连线、保留节点／事件／Branch 的既有合同，支持公开出口连接，不隐式恢复被清除的内部连线。
- 保留独立 C# Fluent API 及旧消费者依赖的公共字段；修订 ADR-0001 和领域术语，不修改 ADR-0002 或执行器。

## 原有未跟踪文件

开始时 `ModuleScriptFacts.cs` 和 `ScriptNodeFactory.cs` 尚未提交，不能视为仓库测试基线。先通过公开入口运行原夹具：15 个用例通过。迁移前保存完整原文至 `originals/*.txt`，不是新语法示例，也不参与编译。

原始 SHA-256（备份后逐一比对相同）：

- ModuleScriptFacts.cs：`916c358d9ba8e40027cf21569e4c2454d504c4adc566cc2377d13913440243aa`
- ScriptNodeFactory.cs：`80848f399ed480e791a8d14adbb9c859d0f07372a8020efc9d78890a8ca271f2`

交付纳入轻量真实节点工厂；原有入口、发布者、隔离及非法映射不变量迁移至新 DSL，不删除或跳过测试以掩盖失败。新增 ModuleDslFacts／ModuleLoadingFacts 补齐本票合同。

## 验证

测试只经过 `FSMEngine` 的公开构建、文件加载、Transform、Try 和结果查询入口，使用实际 BaseFSMNode 分支与转移，不 mock 编译器或断言 ANTLR 内部结构。

- TDD：显式实例闭环、严格语法失败、名称碰撞、导入菱形依赖、重复接口、大小写事件身份均记录失败后修复为通过；本地日志为 `red-*.log`／`green-*.log`（日志按仓库规则不入 git）。
- 模块测试：`dotnet test tests/StateMachineTest/StateMachineTest.csproj --nologo --filter FullyQualifiedName~Module`，71 通过，0 失败，0 跳过。
- 相关构建：`dotnet build AnotherFSM.sln --nologo`，0 错误。旧公共模型成员保留，现有项目全部可编译。
- 最终全量：`dotnet test AnotherFSM.sln --nologo`，82 通过，0 失败，0 跳过。
- 构建存在既有告警：OpenAPI 2.0.0 的 NU1903、分析器可空上下文与打包告警；本票没有扩大为依赖升级。
- `tests/FSMScriptAnalyzerTest` 等消费者控制台示例仅进行构建，不把其旧模块语法运行结果宣称通过；其迁移与真实生成／编译／初始化合同属于 T2。

## 双轴审查

### Standards

首轮未发现硬规则违反。增量复核提出 1 项低优先级 Feature Envy／Duplicated Code 建议：把公开接口查询统一到 resolver。已采纳为 `FindPublicOwner`，审查员复核确认 smell 已消除、无剩余硬违规。

### Spec

首轮发现 3 项已核实问题，主代理均核对代码并通过公开入口复现：

1. 局部事件遮蔽真实限定实例目标。
2. 仅按字符串前缀把不重名的普通点号名称当作模块成员而拒绝。
3. import 别名静默覆盖已有模块定义。

逐项补充红绿测试并修复；另覆盖不相关局部模块前缀同样不得遮蔽真实全局实例／公开出口，以及普通点号节点／事件保持大小写敏感身份。首轮三条均已复核通过。复核又发现两个同类遗漏：最长实例词形前缀会遮蔽较短实例的真实点号公开接口，以及 `module ... as Alias` 仍能覆盖导入别名。均经主代理公开入口复现后补红绿测试修复：按所有实际公开接口筛选有效归属、歧义报错；编译轮次内声明／导入名称冲突拒绝，外部注册占位解析使用明确来源身份。两项均已复核通过。最后复核发现同源 inline 模块别名跨构建轮次按对象引用误判碰撞，已补连续两次公开构建的红绿测试：别名按稳定模块来源身份刷新，不同模块来源仍拒绝覆盖；最后增量已由审查员复核并实际运行 4 个针对性用例通过，无剩余已核实问题。Spec 轴累计 6 项已核实发现全部修复并复核关闭；Standards 轴 0 硬违规，1 项维护性建议已采纳并复核关闭。
