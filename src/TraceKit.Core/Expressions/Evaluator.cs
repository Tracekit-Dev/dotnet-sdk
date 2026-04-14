using System.Text.RegularExpressions;

namespace TraceKit.Core.Expressions;

/// <summary>
/// Evaluates portable-subset expressions locally without server round-trip.
/// Implements a recursive-descent parser for the TraceKit expression spec.
/// </summary>
public static class Evaluator
{
    private static readonly Regex FunctionCallPattern = new(@"\b[a-zA-Z_]\w*\s*\(", RegexOptions.Compiled);
    private static readonly Regex MatchesKeyword = new(@"\bmatches\b", RegexOptions.Compiled);
    private static readonly Regex ArrayIndexPattern = new(@"\[\d", RegexOptions.Compiled);
    private static readonly Regex CompoundAssignPattern = new(@"[+\-*/]=", RegexOptions.Compiled);

    /// <summary>
    /// Returns true if the expression can be evaluated locally by the SDK.
    /// Returns false for expressions containing function calls, regex operators, assignment,
    /// array indexing, ternary, range, template literals, or bitwise operators.
    /// </summary>
    public static bool IsSDKEvaluable(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return true;

        // Function calls: word followed by opening paren
        if (FunctionCallPattern.IsMatch(expression)) return false;

        // Regex match keyword
        if (MatchesKeyword.IsMatch(expression)) return false;

        // Regex operator =~
        if (expression.Contains("=~")) return false;

        // Bitwise NOT ~ (but not inside =~, already handled above)
        for (int i = 0; i < expression.Length; i++)
        {
            if (expression[i] == '~' && (i == 0 || expression[i - 1] != '='))
                return false;
        }

        // Bitwise AND: single & not part of &&
        for (int i = 0; i < expression.Length; i++)
        {
            if (expression[i] == '&')
            {
                if (i + 1 < expression.Length && expression[i + 1] == '&')
                {
                    i++; // skip &&
                    continue;
                }
                return false;
            }
        }

        // Bitwise OR: single | not part of ||
        for (int i = 0; i < expression.Length; i++)
        {
            if (expression[i] == '|')
            {
                if (i + 1 < expression.Length && expression[i + 1] == '|')
                {
                    i++; // skip ||
                    continue;
                }
                return false;
            }
        }

        // Bit shift
        if (expression.Contains("<<") || expression.Contains(">>")) return false;

        // Template literals
        if (expression.Contains("${")) return false;

        // Range operator
        if (expression.Contains("..")) return false;

        // Ternary
        if (expression.Contains('?')) return false;

        // Array indexing [N]
        if (ArrayIndexPattern.IsMatch(expression)) return false;

        // Compound assignment
        if (CompoundAssignPattern.IsMatch(expression)) return false;

        return true;
    }

    /// <summary>
    /// Evaluates a condition expression and returns a boolean.
    /// Empty expressions return true (no condition = always fire).
    /// Throws <see cref="UnsupportedExpressionException"/> for server-only expressions.
    /// </summary>
    public static bool EvaluateCondition(string expression, Dictionary<string, object?> env)
    {
        if (string.IsNullOrWhiteSpace(expression)) return true;

        if (!IsSDKEvaluable(expression))
            throw new UnsupportedExpressionException(expression);

        var result = EvaluateExpression(expression, env);

        return result switch
        {
            bool b => b,
            null => false,
            _ => throw new InvalidOperationException($"Condition must evaluate to bool, got {result.GetType().Name}")
        };
    }

    /// <summary>
    /// Evaluates an expression and returns the raw result.
    /// Throws <see cref="UnsupportedExpressionException"/> for server-only expressions.
    /// </summary>
    public static object? EvaluateExpression(string expression, Dictionary<string, object?> env)
    {
        if (string.IsNullOrWhiteSpace(expression)) return null;

        if (!IsSDKEvaluable(expression))
            throw new UnsupportedExpressionException(expression);

        var tokens = Tokenize(expression);
        var parser = new Parser(tokens, env);
        return parser.ParseExpression();
    }

    /// <summary>
    /// Evaluates multiple expressions. Results keyed by expression string.
    /// On error, null is stored for that expression.
    /// </summary>
    public static Dictionary<string, object?> EvaluateExpressions(List<string> expressions, Dictionary<string, object?> env)
    {
        var results = new Dictionary<string, object?>();
        foreach (var expr in expressions)
        {
            try
            {
                results[expr] = EvaluateExpression(expr, env);
            }
            catch
            {
                results[expr] = null;
            }
        }
        return results;
    }

    #region Tokenizer

    private enum TokenType
    {
        Number, String, Bool, Nil, Identifier, Dot,
        Eq, Neq, Lt, Gt, Lte, Gte,
        And, Or, Not,
        Plus, Minus, Star, Slash,
        LParen, RParen, LBracket, RBracket,
        In, // membership operator
        Eof
    }

    private record Token(TokenType Type, string Value, int Pos);

    private static List<Token> Tokenize(string input)
    {
        var tokens = new List<Token>();
        int i = 0;

        while (i < input.Length)
        {
            // Skip whitespace
            if (char.IsWhiteSpace(input[i])) { i++; continue; }

            // Two-char operators
            if (i + 1 < input.Length)
            {
                var two = input.Substring(i, 2);
                switch (two)
                {
                    case "==": tokens.Add(new Token(TokenType.Eq, "==", i)); i += 2; continue;
                    case "!=": tokens.Add(new Token(TokenType.Neq, "!=", i)); i += 2; continue;
                    case "<=": tokens.Add(new Token(TokenType.Lte, "<=", i)); i += 2; continue;
                    case ">=": tokens.Add(new Token(TokenType.Gte, ">=", i)); i += 2; continue;
                    case "&&": tokens.Add(new Token(TokenType.And, "&&", i)); i += 2; continue;
                    case "||": tokens.Add(new Token(TokenType.Or, "||", i)); i += 2; continue;
                }
            }

            // Single-char operators
            switch (input[i])
            {
                case '<': tokens.Add(new Token(TokenType.Lt, "<", i)); i++; continue;
                case '>': tokens.Add(new Token(TokenType.Gt, ">", i)); i++; continue;
                case '!': tokens.Add(new Token(TokenType.Not, "!", i)); i++; continue;
                case '+': tokens.Add(new Token(TokenType.Plus, "+", i)); i++; continue;
                case '-':
                    // Check if this is a negative number literal (not subtraction)
                    if (tokens.Count == 0 || IsOperatorToken(tokens[^1].Type))
                    {
                        // Negative number
                        int numStart = i;
                        i++;
                        while (i < input.Length && (char.IsDigit(input[i]) || input[i] == '.'))
                            i++;
                        tokens.Add(new Token(TokenType.Number, input[numStart..i], numStart));
                        continue;
                    }
                    tokens.Add(new Token(TokenType.Minus, "-", i)); i++; continue;
                case '*': tokens.Add(new Token(TokenType.Star, "*", i)); i++; continue;
                case '/': tokens.Add(new Token(TokenType.Slash, "/", i)); i++; continue;
                case '(': tokens.Add(new Token(TokenType.LParen, "(", i)); i++; continue;
                case ')': tokens.Add(new Token(TokenType.RParen, ")", i)); i++; continue;
                case '[': tokens.Add(new Token(TokenType.LBracket, "[", i)); i++; continue;
                case ']': tokens.Add(new Token(TokenType.RBracket, "]", i)); i++; continue;
                case '.': tokens.Add(new Token(TokenType.Dot, ".", i)); i++; continue;
            }

            // String literals (double or single quotes)
            if (input[i] == '"' || input[i] == '\'')
            {
                var quote = input[i];
                int start = i;
                i++;
                var sb = new System.Text.StringBuilder();
                while (i < input.Length && input[i] != quote)
                {
                    if (input[i] == '\\' && i + 1 < input.Length)
                    {
                        sb.Append(input[i + 1]);
                        i += 2;
                    }
                    else
                    {
                        sb.Append(input[i]);
                        i++;
                    }
                }
                if (i < input.Length) i++; // skip closing quote
                tokens.Add(new Token(TokenType.String, sb.ToString(), start));
                continue;
            }

            // Numbers
            if (char.IsDigit(input[i]))
            {
                int start = i;
                while (i < input.Length && (char.IsDigit(input[i]) || input[i] == '.'))
                    i++;
                tokens.Add(new Token(TokenType.Number, input[start..i], start));
                continue;
            }

            // Identifiers and keywords
            if (char.IsLetter(input[i]) || input[i] == '_')
            {
                int start = i;
                while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_'))
                    i++;
                var word = input[start..i];
                var tokenType = word switch
                {
                    "true" => TokenType.Bool,
                    "false" => TokenType.Bool,
                    "nil" => TokenType.Nil,
                    "null" => TokenType.Nil,
                    "in" => TokenType.In,
                    _ => TokenType.Identifier
                };
                tokens.Add(new Token(tokenType, word, start));
                continue;
            }

            // Unknown character -- skip
            i++;
        }

        tokens.Add(new Token(TokenType.Eof, "", input.Length));
        return tokens;
    }

    private static bool IsOperatorToken(TokenType type)
    {
        return type is TokenType.Eq or TokenType.Neq or TokenType.Lt or TokenType.Gt
            or TokenType.Lte or TokenType.Gte or TokenType.And or TokenType.Or
            or TokenType.Not or TokenType.Plus or TokenType.Minus or TokenType.Star
            or TokenType.Slash or TokenType.LParen or TokenType.LBracket or TokenType.In;
    }

    #endregion

    #region Parser (recursive descent)

    private class Parser
    {
        private readonly List<Token> _tokens;
        private readonly Dictionary<string, object?> _env;
        private int _pos;

        public Parser(List<Token> tokens, Dictionary<string, object?> env)
        {
            _tokens = tokens;
            _env = env;
            _pos = 0;
        }

        private Token Current => _pos < _tokens.Count ? _tokens[_pos] : _tokens[^1];

        private Token Consume()
        {
            var t = Current;
            _pos++;
            return t;
        }

        private bool Match(TokenType type)
        {
            if (Current.Type == type)
            {
                _pos++;
                return true;
            }
            return false;
        }

        // Entry point: Or expression (lowest precedence)
        public object? ParseExpression() => ParseOr();

        // ||
        private object? ParseOr()
        {
            var left = ParseAnd();
            while (Current.Type == TokenType.Or)
            {
                Consume();
                var right = ParseAnd();
                left = ToBool(left) || ToBool(right);
            }
            return left;
        }

        // &&
        private object? ParseAnd()
        {
            var left = ParseEquality();
            while (Current.Type == TokenType.And)
            {
                Consume();
                var right = ParseEquality();
                left = ToBool(left) && ToBool(right);
            }
            return left;
        }

        // == !=
        private object? ParseEquality()
        {
            var left = ParseComparison();
            while (Current.Type is TokenType.Eq or TokenType.Neq)
            {
                var op = Consume();
                var right = ParseComparison();
                left = op.Type == TokenType.Eq ? AreEqual(left, right) : !AreEqual(left, right);
            }
            return left;
        }

        // < > <= >=
        private object? ParseComparison()
        {
            var left = ParseMembership();
            while (Current.Type is TokenType.Lt or TokenType.Gt or TokenType.Lte or TokenType.Gte)
            {
                var op = Consume();
                var right = ParseMembership();
                left = CompareValues(left, right, op.Type);
            }
            return left;
        }

        // "key" in map
        private object? ParseMembership()
        {
            var left = ParseAdditive();
            if (Current.Type == TokenType.In)
            {
                Consume();
                var right = ParseAdditive();
                return EvalIn(left, right);
            }
            return left;
        }

        // + -
        private object? ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (Current.Type is TokenType.Plus or TokenType.Minus)
            {
                var op = Consume();
                var right = ParseMultiplicative();
                if (op.Type == TokenType.Plus)
                {
                    // String concatenation if either operand is a string
                    if (left is string || right is string)
                        left = $"{left}{right}";
                    else
                        left = ToDouble(left) + ToDouble(right);
                }
                else
                {
                    left = ToDouble(left) - ToDouble(right);
                }
            }
            return left;
        }

        // * /
        private object? ParseMultiplicative()
        {
            var left = ParseUnary();
            while (Current.Type is TokenType.Star or TokenType.Slash)
            {
                var op = Consume();
                var right = ParseUnary();
                if (op.Type == TokenType.Star)
                    left = ToDouble(left) * ToDouble(right);
                else
                    left = ToDouble(left) / ToDouble(right);
            }
            return left;
        }

        // ! (unary not)
        private object? ParseUnary()
        {
            if (Current.Type == TokenType.Not)
            {
                Consume();
                var val = ParseUnary();
                return !ToBool(val);
            }
            return ParsePrimary();
        }

        // Literals, identifiers, parens, bracket access
        private object? ParsePrimary()
        {
            var token = Current;

            switch (token.Type)
            {
                case TokenType.Number:
                    Consume();
                    return ParseNumber(token.Value);

                case TokenType.String:
                    Consume();
                    return token.Value;

                case TokenType.Bool:
                    Consume();
                    return token.Value == "true";

                case TokenType.Nil:
                    Consume();
                    return null;

                case TokenType.LParen:
                    Consume();
                    var val = ParseExpression();
                    Match(TokenType.RParen);
                    return val;

                case TokenType.Identifier:
                    return ParseIdentifier();

                default:
                    Consume(); // skip unknown
                    return null;
            }
        }

        private object? ParseIdentifier()
        {
            var name = Consume().Value;

            // Resolve from environment
            object? value = _env.TryGetValue(name, out var v) ? v : null;

            // Chain dot access and bracket access
            while (Current.Type is TokenType.Dot or TokenType.LBracket)
            {
                if (Current.Type == TokenType.Dot)
                {
                    Consume();
                    if (Current.Type == TokenType.Identifier)
                    {
                        var prop = Consume().Value;
                        value = GetProperty(value, prop);
                    }
                    else
                    {
                        value = null;
                    }
                }
                else if (Current.Type == TokenType.LBracket)
                {
                    Consume();
                    var key = ParseExpression();
                    Match(TokenType.RBracket);
                    if (key is string sKey)
                        value = GetProperty(value, sKey);
                    else
                        value = null;
                }
            }

            return value;
        }

        private static object? GetProperty(object? obj, string key)
        {
            // Null-safe: accessing property on null returns null
            if (obj == null) return null;

            if (obj is Dictionary<string, object?> dict)
                return dict.TryGetValue(key, out var v) ? v : null;

            if (obj is IDictionary<string, object> dict2)
                return dict2.TryGetValue(key, out var v2) ? v2 : null;

            return null;
        }

        private static object ParseNumber(string value)
        {
            if (value.Contains('.'))
                return double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            return long.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static bool ToBool(object? value)
        {
            return value switch
            {
                bool b => b,
                null => false,
                _ => throw new InvalidOperationException($"Cannot convert {value.GetType().Name} to bool")
            };
        }

        private static double ToDouble(object? value)
        {
            return value switch
            {
                long l => l,
                int i => i,
                double d => d,
                float f => f,
                null => 0,
                _ => Convert.ToDouble(value)
            };
        }

        private static bool AreEqual(object? left, object? right)
        {
            if (left == null && right == null) return true;
            if (left == null || right == null) return false;

            // Numeric promotion: int to float
            if (IsNumeric(left) && IsNumeric(right))
                return Math.Abs(ToDouble(left) - ToDouble(right)) < 0.0001;

            // Strict type comparison -- no cross-type coercion
            if (left.GetType() != right.GetType()) return false;

            return left.Equals(right);
        }

        private static bool CompareValues(object? left, object? right, TokenType op)
        {
            // Mixed-type or null comparisons return false per spec
            if (left == null || right == null) return false;

            if (IsNumeric(left) && IsNumeric(right))
            {
                var l = ToDouble(left);
                var r = ToDouble(right);
                return op switch
                {
                    TokenType.Lt => l < r,
                    TokenType.Gt => l > r,
                    TokenType.Lte => l <= r,
                    TokenType.Gte => l >= r,
                    _ => false
                };
            }

            if (left is string ls && right is string rs)
            {
                var cmp = string.Compare(ls, rs, StringComparison.Ordinal);
                return op switch
                {
                    TokenType.Lt => cmp < 0,
                    TokenType.Gt => cmp > 0,
                    TokenType.Lte => cmp <= 0,
                    TokenType.Gte => cmp >= 0,
                    _ => false
                };
            }

            // Incompatible types return false per spec
            return false;
        }

        private static bool EvalIn(object? key, object? map)
        {
            if (key is not string sKey) return false;

            return map switch
            {
                Dictionary<string, object?> dict => dict.ContainsKey(sKey),
                IDictionary<string, object> dict2 => dict2.ContainsKey(sKey),
                _ => false
            };
        }

        private static bool IsNumeric(object? value)
        {
            return value is long or int or double or float;
        }
    }

    #endregion
}
