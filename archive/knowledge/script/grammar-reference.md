---
name: grammar-reference
description: StateMachineScript.g4 完整文法参考，含模块扩展
metadata:
  type: knowledge
---

# StateMachineScript 文法参考

`StateMachineScript.g4` — ANTLR4 脚本文法定义

## 顶层结构

```
machine
    : (import_statement | module_statement | expression | namespace)* EOF
    ;
```

脚本由四种顶层元素组成，按任意顺序排列，以 EOF 结束。

## 模块与导入

```
import_statement
    : 'import' STRING ('as' STRING)? SEMICOLON
    ;

module_statement
    : 'module' STRING ('as' STRING)? '{' module_body '}'
    ;

module_body
    : (input_declaration | output_declaration | terminal_declaration
       | expression | event_statement)*
    ;

input_declaration
    : 'input' STRING (',' STRING)* SEMICOLON
    ;

output_declaration
    : 'output' STRING (',' STRING)* SEMICOLON
    ;

terminal_declaration
    : 'terminal' STRING (',' STRING)* SEMICOLON
    ;
```

## 命名空间

```
namespace
    : 'namespace' STRING '{' expression* '}'
    ;
```

内部节点名自动加 `STRING.` 前缀。

## 表达式

```
expression
    : state_statement
    | event_statement
    ;

state_statement
    : 'def' STRING 'as' STRING state '{' state_branch* '}'
    | 'def' STRING '(' STRING ')' '{' state_branch* '}'
    ;

event_statement
    : 'def' STRING 'as' 'event' SEMICOLON
    ;
```

两种节点定义语法：
- `def NodeName as NodeType state { ... }` — 完整语法
- `def NodeName(NodeType) { ... }` — 简写语法

## 分支定义

```
state_branch
    : 'branch' INT TRIGGER STRING SEMICOLON           #DefBranch
    | branch_type=(INT|NONE|SUCCESS|FAILED|ERROR|BREAK|CANCEL)
        TRIGGER STRING SEMICOLON                       #DefBranch2
    | 'Pos' COLON position SEMICOLON                   #PosDef
    | 'Color' COLON CODESTRING SEMICOLON               #ColorDef
    | 'Type' COLON STRING SEMICOLON                    #TypeDef
    | 'FlowID' COLON (GUID|STRING|INT) SEMICOLON       #FlowIDDef
    | STRING TRIGGER STRING SEMICOLON                  #NamedOutputMapDef
    | '[' STRING TRIGGER STRING ']' SEMICOLON          #GroudFSMDef
    ;
```

| 规则 | 含义 |
|------|------|
| `DefBranch` | `branch 1->NextEvent;` — 索引绑定事件 |
| `DefBranch2` | `1->NextEvent;` — 简写（无 branch 关键字） |
| `PosDef` | `Pos:(100, 200);` — 画布位置 |
| `ColorDef` | `Color:"light-blue";` — 颜色 |
| `TypeDef` | `Type:"Start";` — 节点类型标签 |
| `FlowIDDef` | `FlowID:guid-or-string;` — 流 ID |
| `NamedOutputMapDef` | `OutputEvent->ExternalEvent;` — 模块输出映射 |
| `GroudFSMDef` | `[Start->NodeName];` — GroupNode 定义 |

## 转移定义

```
transition
    : STRING TRIGGER STRING 'to' STRING SEMICOLON
    ;
```

格式：`Event->SourceNode to TargetNode;`

## Lexer 关键字

```
MODULE  : 'module'    IMPORT  : 'import'
INPUT   : 'input'     OUTPUT : 'output'
NONE    : 'none'      NEXT   : 'next'
SUCCESS : 'success'   FAILED : 'failed'
ERROR   : 'error'     BREAK  : 'break'
CANCEL  : 'cancel'
```

## 数据类型

| 类型 | 规则 | 说明 |
|------|------|------|
| STRING | `[_A-Za-zΑ-ω一-龥][0-9_A-Za-zΑ-ω一-龥.]*` | 标识符，支持中英文和 `.` |
| INT | `[0-9]+` | 整数 |
| DOUBLE | `[0-9]+'.'[0-9]+` | 浮点数 |
| GUID | `HEX8-HEX4-HEX4-HEX4-HEX12` | UUID 格式 |
| CODESTRING | `"...".*?"..."` | 引号字符串 |

## 注释

- 行注释：`// ...`
- 块注释：`/* ... */`

## 交叉引用

- [[script-parsing]] — 文法的编译流程
- [[module-expansion-pipeline]] — import/module 语义展开
- [[composition]] — GroudFSMDef 对应的 GroupNode
