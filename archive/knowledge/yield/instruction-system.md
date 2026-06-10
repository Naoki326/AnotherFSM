---
name: instruction-system
description: IYieldAction 接口设计与全部 Yield 指令类型的语义
metadata:
  type: knowledge
---

# Yield 指令系统

`IYieldAction` `Yield` — 异步枚举节点中的执行控制指令

## IYieldAction 接口

```csharp
public interface IYieldAction {
    YieldEnum Result { get; set; }     // 执行结果枚举
    bool IsMoveNext { get; }           // 是否继续执行下一步
    FSMNodeContext Context { get; set; } // 注入的节点上下文
    Task AfterYieldAsync();            // yield 后立即调用
    Task BeforeNextAsync();            // 下次 MoveNext 前调用
}
```

### 双阶段执行

1. **AfterYieldAsync** — yield return 后立即执行（如发送事件）
2. **BeforeNextAsync** — 下次迭代前执行（如检查暂停、执行延时）

### IsMoveNext

- `true` → 继续执行下一个 yield
- `false` → 停止枚举（用于 Retry/Restart 等需要重新开始的情况）

## Yield 静态工厂

`Yield` 类提供所有指令的创建方法：

## 指令类型一览

### 流程控制

| 指令 | 工厂方法 | 语义 |
|------|----------|------|
| `YieldNone` | `Yield.None` | 空操作，提供一个暂停检查点 |
| `YieldResult` | `Yield.Result(enum)` | 返回指定 YieldEnum 结果 |

### 事件发送

| 指令 | 工厂方法 | 语义 |
|------|----------|------|
| `YieldEvent` | `Yield.Event(int/Enum, data?)` | 发送事件到 FSMEngine，触发 transition |
| — | `Yield.Next` | 等价 `Yield.Event(FSMEnum.Next)` |
| — | `Yield.Error` | 等价 `Yield.Event(FSMEnum.Error)` |

### 时间控制

| 指令 | 工厂方法 | 语义 |
|------|----------|------|
| `YieldDelay` | `Yield.Delay(ms)` / `Yield.Delay(timespan)` | 异步延时，支持 CancellationToken |
| `YieldPriority` | `Yield.Priority(enum)` | 检查暂停锚点是否匹配，匹配则暂停 |

### 异常处理

| 指令 | 工厂方法 | 语义 |
|------|----------|------|
| `YieldTryCatch` | `Yield.TryCatch(action, handler)` | 捕获异常并调用处理函数 |
| `YieldRetryIfFailed` | `Yield.RetryIfFailed` | 失败时从当前 yield 点重试（IsMoveNext=false） |
| `YieldRestartIfFailed` | `Yield.RestartIfFailed` | 失败时从节点起点重新执行 |
| `YieldPauseRetryIfFailed` | `Yield.PauseRetryIfFailed` | 失败时暂停并标记重试 |

### 暂停/恢复

| 指令 | 工厂方法 | 语义 |
|------|----------|------|
| `YieldPause` | `Yield.Pause` | 主动暂停节点执行 |
| `YieldPauseToNodeStart` | `Yield.PauseToNodeStart` | 暂停并回到节点起点 |
| `YieldToNodeStart` | `Yield.ToNodeStart` | 回到节点起点（不暂停） |
| `YieldRestoreIfPaused` | `Yield.RestoreIfPaused` | 暂停后恢复执行的回调 |
| `YieldRetryIfPause` | `Yield.RetryIfPause` | 暂停后从当前 yield 点重试 |

## YieldEnum 结果枚举

| 值 | 含义 |
|----|------|
| `None` | 无操作 |
| `Next` | 正常继续 |
| `Pause` | 暂停 |
| `Error` | 错误 |
| `Retry` | 重试 |
| `Cancel` | 取消 |

## 典型用法

```csharp
protected override async IAsyncEnumerable<IYieldAction> ExecuteEnumerable() {
    yield return Yield.None;                    // 暂停检查点

    yield return Yield.TryCatch(async () => {
        await DoWork();
    }, ex => {
        LogError(ex);
    });

    yield return Yield.Delay(1000);             // 延时 1 秒
    yield return Yield.Priority("checkpoint");  // 检查暂停锚点

    PublishEvent(FSMEnum.Next);                 // 触发转移
    yield break;
}
```

## 交叉引用

- [[scheduling-lifecycle]] — FSMExecutor 如何响应暂停/恢复
- [[hierarchy-interfaces]] — AsyncEnumFSMNode 的 yield 执行模型
