# #34 复杂项目与外部依赖关联

状态：实施中，未完成验收或远端交付。基线 `bfa3fc7f5d4580549e7a4b62bfed47c84bd87f4b`。

仅实现 [#34](https://github.com/Naoki326/AnotherFSM/issues/34)。#33 的 DSL 图复用；#35 的完整编写辅助和 #36 的最终性能预算不提前宣称完成。不修改已关闭父规格或地图。

## 已确认测试 seam

1. 共享核心公开查询：完整类型身份、上下文隔离、显式覆盖与冲突。
2. 目标 Rider SDK：实际消费依赖、多宿主/TFM、原生搜索及失效。
3. 真实 Rider 正式操作：选择、导航、来源/降级说明、结果失效和安全哨兵。

核心、SDK 编译、受控夹具准备、真实 Rider、性能分层报告；构建不代表产品验收。

## 可选配置

普通项目无需配置。特殊项目在宿主 `.csproj` 旁提交 `anotherfsm.json`，不自动生成，不复制完整注册表。版本 1：

```json
{
  "version": 1,
  "contexts": [{
    "host": "App.csproj",
    "targetFramework": "net8.0",
    "scripts": ["../Shared/flow.fsm"],
    "keyComparison": "OrdinalIgnoreCase",
    "mappings": [{
      "key": "Special",
      "type": {
        "project": "../Nodes/Nodes.csproj",
        "clrName": "Nodes.Container+Worker",
        "alias": "global"
      }
    }],
    "modules": [{ "name": "Registered", "file": "../Modules/actual.fsm" }]
  }]
}
```

- 所有路径相对于配置目录，`scripts` 为精确实体文件列表；省略时适用于该宿主全部脚本。`host` 必需；省略 `targetFramework` 时适用于该宿主全部 TFM。支持的简写为 `net8.0`、`net10.0`、`netstandard2.0`。
- 默认比较为 `OrdinalIgnoreCase`，可选 `Ordinal`；不改变 DSL 名称本身的规则。
- 类型目标必须指定 `project`（项目文件路径）或 `assembly`（完整程序集身份），二选一；`clrName` 使用嵌套/泛型 CLR 声明名。外部类型不会因此被插件额外加载。
- `typeArguments` 为递归类型目标数组，顺序为外层到内层、从左到右。必须全部可解析、具体且满足宿主验证的约束。开放泛型不能直接绑定。
- `alias` 默认 `global`，必须在实际消费引用中存在。完整身份不降成显示名比较。
- 显式映射替换同一上下文/比较规则下该键的默认特性关联，原生反查与导航共用规则；展示配置来源。重复显式映射保留歧义，无效目标不回退到特性。
- `modules` 在根脚本处理前提供文件注册，import 也使用同一精确映射。重复注册不取首项；默认相对 import 规则保持不变。
- 配置损坏/冲突可见，不执行注册逻辑来补齐信息。个人导航选择只影响本次操作，不写共享配置。

## 安全与来源边界

只消费宿主已有项目/依赖 PSI 和编辑缓冲区。不执行注册、工厂、getter、特性构造器或业务程序；不由插件发起业务构建/restore/生成器、程序集加载、源码下载或凭据流程。夹具的显式构建/打包属于受控准备，与插件分析严格区分。

外部类型绑定和源码可用性分开。项目源码、已验证外部源码、未验证候选、反编译和元数据不得混称。导航成功、SourceCSPath、PDB 或 Source Link 的存在不提升源码身份；无法确认离线/权限原因时不猜测。真实宿主降级场景仍待本票逐项验收。

## 验收台账

待补充本轮最终 SDK、真实 Rider、全套回归、双轴审查及远端交付回读证据。不得根据本文件中的格式说明将本票标为完成。
