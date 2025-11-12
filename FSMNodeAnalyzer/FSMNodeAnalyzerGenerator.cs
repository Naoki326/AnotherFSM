using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace FSMNodeAnalyzer
{
    /// <summary>
    /// 源生成器，用于分析项目中所有派生自AbstractFSMNode的类
    /// 并生成包含类信息的帮助类
    /// </summary>
    [Generator]
    public class FSMNodeAnalyzerGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // 获取所有语法树中的类声明
            var classDeclarations = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: (node, _) => IsClassDeclaration(node),
                    transform: (context, ct) => GetClassInfo(context, ct))
                .Where(classInfo => classInfo != null);

            // 收集所有派生自AbstractFSMNode的类
            var fsmNodeClasses = classDeclarations.Collect();

            // 仅为每个节点生成 partial 分部实现（直接在节点类上实现接口并提供源路径）
            context.RegisterSourceOutput(fsmNodeClasses, GenerateNodePartials);
        }

        private bool IsClassDeclaration(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax;
        }

        private ClassInfo GetClassInfo(GeneratorSyntaxContext context, CancellationToken cancellationToken)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            var semanticModel = context.SemanticModel;

            // 获取类的语义信息
            var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration, cancellationToken);
            if (classSymbol == null) return null;

            // 检查是否派生自AbstractFSMNode
            if (!IsDerivedFromAbstractFSMNode(classSymbol))
            {
                return null;
            }

            // 获取泛型参数信息
            var genericParameters = GetGenericParameters(classSymbol);
            var baseTypes = GetBaseTypes(classSymbol);
            var properties = GetFSMProperties(classSymbol);
            var events = GetYieldEvents(classDeclaration, semanticModel, cancellationToken);

            return new ClassInfo
            {
                ClassName = classSymbol.Name,
                FullTypeName = classSymbol.ToDisplayString(),
                FilePath = context.Node.SyntaxTree.FilePath,
                Namespace = classSymbol.ContainingNamespace?.ToString() ?? "",
                GenericParameters = genericParameters,
                BaseTypes = baseTypes,
                Properties = properties,
                Events = events,
                IsGeneric = classSymbol.IsGenericType,
                TypeParametersCount = classSymbol.TypeParameters.Length
            };
        }

        private bool IsDerivedFromAbstractFSMNode(ITypeSymbol classSymbol)
        {
            var baseType = classSymbol.BaseType;
            while (baseType != null)
            {
                if (baseType.Name == "AbstractFSMNode" &&
                    baseType.ContainingNamespace?.ToString() == "StateMachine")
                {
                    return true;
                }
                baseType = baseType.BaseType;
            }
            return false;
        }

        private List<string> GetGenericParameters(ITypeSymbol classSymbol)
        {
            var parameters = new List<string>();
            if (classSymbol is INamedTypeSymbol namedType)
            {
                foreach (var param in namedType.TypeParameters)
                {
                    parameters.Add(param.Name);
                }
            }
            return parameters;
        }

        private List<string> GetBaseTypes(ITypeSymbol classSymbol)
        {
            var baseTypes = new List<string>();
            var baseType = classSymbol.BaseType;
            while (baseType != null && baseType.Name != "Object")
            {
                baseTypes.Add(baseType.ToDisplayString());
                baseType = baseType.BaseType;
            }
            return baseTypes;
        }

        private List<PropertyInfo> GetFSMProperties(ITypeSymbol classSymbol)
        {
            var properties = new List<PropertyInfo>();
            foreach (var member in classSymbol.GetMembers())
            {
                if (member is IPropertySymbol property)
                {
                    var fsmAttr = property.GetAttributes()
                        .FirstOrDefault(attr => attr.AttributeClass?.Name == "FSMPropertyAttribute");

                    if (fsmAttr != null)
                    {
                        var name = fsmAttr.ConstructorArguments.Length > 0 ?
                            fsmAttr.ConstructorArguments[0].Value?.ToString() : property.Name;
                        var isGeneric = property.Type is INamedTypeSymbol namedType && namedType.IsGenericType;

                        properties.Add(new PropertyInfo
                        {
                            Name = property.Name,
                            DisplayName = name ?? property.Name,
                            Type = property.Type.ToDisplayString(),
                            IsFSMProperty = true,
                            IsGeneric = isGeneric
                        });
                    }
                }
            }
            return properties;
        }

        private void GenerateNodePartials(SourceProductionContext context, ImmutableArray<ClassInfo> classes)
        {
            var list = classes.Where(c => c != null).ToList();
            var partialsCode = GenerateNodePartialsCode(list);
            context.AddSource("FSMNodeSourceIndex.Partials.g.cs", partialsCode);
        }

        // 已移除自动模块注册器生成，改为运行时懒初始化以减少对启动阶段和全局扫描的依赖。

        // 不再生成自定义接口，使用 StateMachine.Interface.IFSMNodeSourceIndex

        private string GenerateNodePartialsCode(List<ClassInfo> classes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            foreach (var c in classes)
            {
                if (string.IsNullOrWhiteSpace(c.FullTypeName) || string.IsNullOrWhiteSpace(c.FilePath))
                    continue;
                var ns = string.IsNullOrWhiteSpace(c.Namespace) ? null : c.Namespace;
                var typeName = c.ClassName;
                var filePathEscaped = c.FilePath.Replace("\"", "\\\"");
                if (!string.IsNullOrEmpty(ns))
                {
                    sb.AppendLine($"namespace {ns}");
                    sb.AppendLine("{");
                }
                {
                    var constEvents = c.Events.Where(e => e.IsConst).ToList();
                    var values = constEvents
                        .Select(e => e.UnderlyingValue)
                        .Where(v => v.HasValue)
                        .Select(v => v.Value)
                        .Distinct()
                        .OrderBy(v => v)
                        .ToList();
                    var eventsArray = values.Count == 0
                        ? "new int[] { }"
                        : $"new int[] {{ {string.Join(", ", values)} }}";
                    sb.AppendLine($"    [global::StateMachine.FSMNodeSourceAttribute(@\"{filePathEscaped}\", {eventsArray})]");
                }
                sb.AppendLine($"    public partial class {typeName} : global::StateMachine.IFSMNodeSourceInfo");
                sb.AppendLine("    {");
                sb.AppendLine($"        public static string StaticSourceCSPath => @\"{filePathEscaped}\";");
                sb.AppendLine($"        public string SourceCSPath => StaticSourceCSPath;");

                // 统一消费的“事件清单”改为 int 列表，并去重
                {
                    sb.AppendLine();
                    if (c.Events != null && c.Events.Count > 0)
                    {
                        sb.AppendLine("        public static readonly global::System.Collections.Generic.IReadOnlyList<int> Events = ");
                        sb.AppendLine("            global::System.Linq.Enumerable.ToList(");
                        sb.AppendLine("                global::System.Linq.Enumerable.OrderBy(");
                        sb.AppendLine("                    global::System.Linq.Enumerable.Distinct(new int[] {");
                        var constEvents = c.Events.Where(e => e.IsConst).ToList();
                        for (int i = 0; i < constEvents.Count; i++)
                        {
                            var ev = constEvents[i];
                            var comma = i < constEvents.Count - 1 ? "," : string.Empty;
                            if (ev.TypeName == "int")
                                sb.AppendLine($"                    {ev.Initializer}{comma}");
                            else
                                sb.AppendLine($"                    (int)({ev.Initializer}){comma}");
                        }
                        sb.AppendLine("                    }),");
                        sb.AppendLine("                    x => x");
                        sb.AppendLine("                )");
                        sb.AppendLine("            );");
                    }
                    else
                    {
                        sb.AppendLine("        public static readonly global::System.Collections.Generic.IReadOnlyList<int> Events = global::System.Array.Empty<int>();");
                    }

                    sb.AppendLine();
                    sb.AppendLine("        global::System.Collections.Generic.IReadOnlyList<int> global::StateMachine.IFSMNodeSourceInfo.Events => Events;");
                }
                sb.AppendLine("    }");
                if (!string.IsNullOrEmpty(ns))
                {
                    sb.AppendLine("}");
                }
            }
            return sb.ToString();
        }

        // 无需 provider 类名生成

        /// <summary>
        /// 类信息
        /// </summary>
        internal class ClassInfo
        {
            public string ClassName { get; set; }
            public string FullTypeName { get; set; }
            public string FilePath { get; set; }
            public string Namespace { get; set; }
            public List<string> GenericParameters { get; set; } = new List<string>();
            public List<string> BaseTypes { get; set; } = new List<string>();
            public List<PropertyInfo> Properties { get; set; } = new List<PropertyInfo>();
            public List<EventInfo> Events { get; set; } = new List<EventInfo>();
            public bool IsGeneric { get; set; }
            public int TypeParametersCount { get; set; }

            public override bool Equals(object obj)
            {
                if (obj is not ClassInfo other) return false;
                return ClassName == other.ClassName && FilePath == other.FilePath;
            }

            public override int GetHashCode()
            {
                return (ClassName + FilePath).GetHashCode();
            }
        }

        /// <summary>
        /// 属性信息
        /// </summary>
        internal class PropertyInfo
        {
            public string Name { get; set; }
            public string DisplayName { get; set; }
            public string Type { get; set; }
            public bool IsFSMProperty { get; set; }
            public bool IsGeneric { get; set; }
        }

        /// <summary>
        /// 事件信息（从 ExecuteEnumerable 的 yield return Yield.Event(...) 中解析）
        /// </summary>
        internal class EventInfo
        {
            public string FieldName { get; set; } = string.Empty;
            public string TypeName { get; set; } = "int"; // 默认 int
            public string Initializer { get; set; } = "0";
            public bool IsConst { get; set; } = true;
            public int? UnderlyingValue { get; set; }
        }

        private List<EventInfo> GetYieldEvents(ClassDeclarationSyntax classDeclaration, SemanticModel semanticModel, CancellationToken ct)
        {
            var events = new List<EventInfo>();
            // 为避免丢失不同枚举类型但底层值相同的事件，使用初始化器维度去重
            var seenByInitializer = new HashSet<string>();

            // 查找名为 ExecuteEnumerable 的方法（支持 IEnumerable<object> / IAsyncEnumerable<object>）
            var methods = classDeclaration.Members
                .OfType<MethodDeclarationSyntax>()
                .Where(m => m.Identifier.Text == "ExecuteEnumerable");

            foreach (var method in methods)
            {
                // 遍历所有 yield return 语句
                foreach (var yieldStmt in method.DescendantNodes().OfType<YieldStatementSyntax>())
                {
                    if (!yieldStmt.Kind().Equals(SyntaxKind.YieldReturnStatement)) continue;
                    var expr = yieldStmt.Expression;
                    if (expr is InvocationExpressionSyntax inv)
                    {
                        // 判断是否调用的是 StateMachine.Yield.Event
                        var symbolInfo = semanticModel.GetSymbolInfo(inv, ct);
                        var methodSymbol = symbolInfo.Symbol as IMethodSymbol;
                        if (methodSymbol == null) continue;
                        if (methodSymbol.Name != "Event") continue;
                        var containingType = methodSymbol.ContainingType;
                        if (containingType == null) continue;
                        var typeFullName = containingType.ToDisplayString();
                        if (containingType.Name != "Yield" || (typeFullName != "StateMachine.Yield" && typeFullName != "global::StateMachine.Yield"))
                        {
                            continue;
                        }

                        // 提取第一个参数（事件索引/枚举）
                        if (inv.ArgumentList.Arguments.Count == 0) continue;
                        var argExpr = inv.ArgumentList.Arguments[0].Expression;

                        // 先尝试枚举成员
                        var argSym = semanticModel.GetSymbolInfo(argExpr, ct).Symbol;
                        if (argSym is IFieldSymbol fieldSym && fieldSym.ContainingType?.TypeKind == TypeKind.Enum)
                        {
                            var enumType = fieldSym.ContainingType;
                            var enumTypeName = enumType.ToDisplayString();
                            var memberName = fieldSym.Name;
                            var initializer = $"{enumTypeName}.{memberName}";
                            var fieldName = MakeIdentifier($"Event_{Sanitize(enumType.Name)}_{Sanitize(memberName)}");
                            var key = $"enum:{initializer}";
                            if (seenByInitializer.Contains(key)) continue;
                            seenByInitializer.Add(key);
                            var value = fieldSym.ConstantValue is int iv ? iv : (int?)null;
                            events.Add(new EventInfo
                            {
                                FieldName = fieldName,
                                TypeName = enumTypeName,
                                Initializer = initializer,
                                IsConst = true,
                                UnderlyingValue = value
                            });
                            continue;
                        }

                        // 再尝试常量 int
                        var constVal = semanticModel.GetConstantValue(argExpr);
                        if (constVal.HasValue && constVal.Value is int intVal)
                        {
                            var key = $"int:{intVal}";
                            if (seenByInitializer.Contains(key)) continue;
                            seenByInitializer.Add(key);
                            var fieldName = MakeIdentifier($"Event_{intVal}");
                            events.Add(new EventInfo
                            {
                                FieldName = fieldName,
                                TypeName = "int",
                                Initializer = intVal.ToString(),
                                IsConst = true,
                                UnderlyingValue = intVal
                            });
                            continue;
                        }

                        // 若为其它枚举类型表达式（例如变量/属性），尝试使用其类型名作为字段类型并生成只读字段
                        var typeInfo = semanticModel.GetTypeInfo(argExpr, ct).Type;
                        if (typeInfo != null && typeInfo.TypeKind == TypeKind.Enum)
                        {
                            var typeName = typeInfo.ToDisplayString();
                            var initText = argExpr.ToString();
                            var key = $"expr:{typeName}:{initText}";
                            if (seenByInitializer.Contains(key)) continue;
                            seenByInitializer.Add(key);
                            var fieldName = MakeIdentifier($"Event_{Sanitize(typeInfo.Name)}");
                            events.Add(new EventInfo
                            {
                                FieldName = fieldName,
                                TypeName = typeName,
                                Initializer = initText,
                                IsConst = false,
                                UnderlyingValue = null
                            });
                        }
                    }
                    else if (expr is MemberAccessExpressionSyntax ma)
                    {
                        // 处理 Yield.Next / Yield.Error / Yield.Failed / Yield.Break 等快捷事件
                        var propSymbolInfo = semanticModel.GetSymbolInfo(ma, ct).Symbol as IPropertySymbol;
                        if (propSymbolInfo == null) continue;
                        var containingType = propSymbolInfo.ContainingType;
                        if (containingType == null) continue;
                        var typeFullName = containingType.ToDisplayString();
                        if (containingType.Name != "Yield" || (typeFullName != "StateMachine.Yield" && typeFullName != "global::StateMachine.Yield"))
                        {
                            continue;
                        }
                        var propName = propSymbolInfo.Name;
                        // 映射到 FSMEnum 成员
                        if (propName is "Next" or "Error" or "Failed" or "Break")
                        {
                            var enumTypeName = "StateMachine.FSMEnum";
                            var initializer = $"{enumTypeName}.{propName}";
                            var key = $"enum:{initializer}";
                            if (seenByInitializer.Contains(key)) continue;
                            seenByInitializer.Add(key);
                            var fieldName = MakeIdentifier($"Event_FSMEnum_{Sanitize(propName)}");
                            events.Add(new EventInfo
                            {
                                FieldName = fieldName,
                                TypeName = enumTypeName,
                                Initializer = initializer,
                                IsConst = true,
                                UnderlyingValue = propName switch
                                {
                                    "Next" => 1,
                                    "Error" => -1,
                                    "Failed" => 2,
                                    "Break" => 3,
                                    _ => null
                                }
                            });
                        }
                    }
                }
            }

            return events;
        }

        private static string Sanitize(string name)
        {
            var s = name.Replace('.', '_').Replace('+', '_').Replace('-', '_');
            return s;
        }

        private static string MakeIdentifier(string raw)
        {
            var s = Sanitize(raw);
            // 简单保证标识符有效
            if (!SyntaxFacts.IsValidIdentifier(s))
            {
                var sb = new StringBuilder();
                foreach (var ch in s)
                {
                    sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
                }
                s = sb.ToString();
                if (!SyntaxFacts.IsValidIdentifier(s)) s = "Event_Const";
            }
            return s;
        }
    }
}
