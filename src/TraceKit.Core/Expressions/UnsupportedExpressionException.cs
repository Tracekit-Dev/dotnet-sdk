namespace TraceKit.Core.Expressions;

/// <summary>
/// Thrown when an expression requires server-side evaluation and cannot be evaluated locally.
/// </summary>
public class UnsupportedExpressionException : Exception
{
    public UnsupportedExpressionException(string expression)
        : base($"Unsupported expression requires server-side evaluation: {expression}")
    {
        Expression = expression;
    }

    public string Expression { get; }
}
