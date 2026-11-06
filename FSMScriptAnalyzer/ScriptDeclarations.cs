using System;
using System.Collections.Generic;

namespace FSMScriptAnalyzer
{
    // Declaration projection only: no module expansion, interface resolution or validation.
    // The original script is always passed to Engine, the sole owner of DSL semantics.
    internal sealed class ScriptDeclarations
    {
        public List<NodeDeclaration> Nodes { get; } = new List<NodeDeclaration>();
        public HashSet<string> Events { get; } = new HashSet<string>(StringComparer.Ordinal);
        public List<string> Imports { get; } = new List<string>();

        public static ScriptDeclarations Read(string script)
        {
            var result = new ScriptDeclarations();
            var tokens = Tokenize(script);
            var position = 0;
            ReadScope(tokens, ref position, result, "");
            return result;
        }

        private static void ReadScope(List<string> tokens, ref int position, ScriptDeclarations result, string prefix)
        {
            while (position < tokens.Count && tokens[position] != "}")
            {
                var statement = ReadStatement(tokens, ref position);
                if (statement.Count == 0)
                {
                    if (position < tokens.Count && tokens[position] == "{") SkipBody(tokens, ref position);
                    continue;
                }
                var hasBody = position < tokens.Count && tokens[position] == "{";
                if (statement[0] == "namespace" && statement.Count == 2 && hasBody)
                {
                    position++;
                    ReadScope(tokens, ref position, result, statement[1] + ".");
                    continue;
                }
                if (statement[0] == "import" && statement.Count >= 2)
                    result.Imports.Add(statement[1]);
                else if (statement[0] == "def" && statement.Count >= 3)
                {
                    var name = statement[1];
                    if (!hasBody && statement[statement.Count - 1] == "event")
                        result.Events.Add(prefix + name);
                    else if (hasBody)
                    {
                        // def Name(Type) {...} and def Name as Type state {...}.
                        var type = AnotherFSM.Syntax.DeclarationRules.TypeKey(name, null);
                        if (statement[2] == "(" && statement.Count > 4) type = statement[3];
                        else if (statement[2] == "as" && statement.Count > 4) type = statement[3];
                        result.Nodes.Add(new NodeDeclaration(AnotherFSM.Syntax.DeclarationRules.Qualify(prefix, name), type));
                        position++;
                        ReadBranches(tokens, ref position, result, prefix);
                        continue;
                    }
                }
                else if (statement.Count == 5 && IsTrigger(statement[1]) && statement[3] == "to")
                    result.Events.Add(prefix + statement[0]);
                // A module body is a template, and use's body is metadata, not node declarations.
                // Two-name Output connections are deliberately not projected: their qualified
                // identity (including cross-Namespace resolution) belongs to Engine.
                if (hasBody) SkipBody(tokens, ref position);
            }
            if (position < tokens.Count && tokens[position] == "}") position++;
        }

        private static void ReadBranches(List<string> tokens, ref int position, ScriptDeclarations result, string prefix)
        {
            while (position < tokens.Count && tokens[position] != "}")
            {
                var statement = ReadStatement(tokens, ref position);
                if (statement.Count == 3 && IsBranch(statement[0]) && IsTrigger(statement[1]))
                    result.Events.Add(prefix + statement[2]);
                else if (statement.Count == 4 && statement[0] == "branch" && IsTrigger(statement[2]))
                    result.Events.Add(prefix + statement[3]);
                if (position < tokens.Count && tokens[position] == "{") SkipBody(tokens, ref position);
            }
            if (position < tokens.Count) position++;
        }

        private static bool IsTrigger(string token) => token == "->" || token == "trigger";

        private static bool IsBranch(string token)
        {
            return int.TryParse(token, out _) || token == "none" || token == "success"
                || token == "failed" || token == "error" || token == "break" || token == "cancel";
        }

        private static List<string> ReadStatement(List<string> tokens, ref int position)
        {
            var result = new List<string>();
            while (position < tokens.Count)
            {
                var token = tokens[position];
                if (token == "{" || token == "}") break;
                position++;
                if (token == ";") break;
                result.Add(token);
            }
            return result;
        }

        private static void SkipBody(List<string> tokens, ref int position)
        {
            var depth = 0;
            do
            {
                var token = tokens[position++];
                if (token == "{") depth++;
                else if (token == "}") depth--;
            } while (position < tokens.Count && depth > 0);
        }

        private static List<string> Tokenize(string script)
        {
            var tokens = new List<string>();
            var i = 0;
            while (i < script.Length)
            {
                var ch = script[i];
                if (char.IsWhiteSpace(ch)) { i++; continue; }
                if (ch == '/' && i + 1 < script.Length)
                {
                    if (script[i + 1] == '/')
                    {
                        i += 2;
                        while (i < script.Length && script[i] != '\r' && script[i] != '\n') i++;
                        continue;
                    }
                    if (script[i + 1] == '*')
                    {
                        i += 2;
                        while (i + 1 < script.Length && !(script[i] == '*' && script[i + 1] == '/')) i++;
                        i = Math.Min(script.Length, i + 2);
                        continue;
                    }
                }
                var start = i++;
                if (ch == '"')
                {
                    // CODESTRING is opaque, including comment delimiters and braces.
                    // Match the core lexer's non-greedy quote rule (not C# escape rules).
                    while (i < script.Length && script[i] != '"') i++;
                    if (i < script.Length) i++;
                }
                else if (char.IsLetter(ch) || ch == '_')
                {
                    while (i < script.Length && (char.IsLetterOrDigit(script[i]) || script[i] == '_' || script[i] == '.')) i++;
                }
                else if (char.IsDigit(ch) || (ch == '-' && i < script.Length && char.IsDigit(script[i])))
                {
                    while (i < script.Length && (char.IsDigit(script[i]) || script[i] == '.')) i++;
                }
                else if (ch == '-' && i < script.Length && script[i] == '>') i++;
                tokens.Add(script.Substring(start, i - start));
            }
            return tokens;
        }
    }

    internal sealed class NodeDeclaration
    {
        public string Name { get; }
        public string TypeKey { get; }
        public NodeDeclaration(string name, string typeKey) => (Name, TypeKey) = (name, typeKey);
    }
}
