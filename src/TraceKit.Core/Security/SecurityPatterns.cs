using System.Text.RegularExpressions;

namespace TraceKit.Core.Security;

/// <summary>
/// Regex patterns for detecting sensitive data in snapshots.
/// </summary>
internal static partial class SecurityPatterns
{
    // PII Patterns
    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled)]
    public static partial Regex Email();

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled)]
    public static partial Regex SSN();

    [GeneratedRegex(@"\b\d{4}[- ]?\d{4}[- ]?\d{4}[- ]?\d{4}\b", RegexOptions.Compiled)]
    public static partial Regex CreditCard();

    [GeneratedRegex(@"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b", RegexOptions.Compiled)]
    public static partial Regex Phone();

    // Credential Patterns
    [GeneratedRegex(@"(api[_-]?key|apikey|access[_-]?key)[\s:=]+['\""  ]?([a-zA-Z0-9_-]{20,})['"" ]?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public static partial Regex APIKey();

    [GeneratedRegex(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled)]
    public static partial Regex AWSKey();

    [GeneratedRegex(@"sk_live_[0-9a-zA-Z]{24}", RegexOptions.Compiled)]
    public static partial Regex StripeKey();

    [GeneratedRegex(@"(password|pwd|pass)[\s:=]+['"" ]?([^\s'"" ]{6,})['"" ]?", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public static partial Regex Password();

    [GeneratedRegex(@"eyJ[a-zA-Z0-9_-]+\.eyJ[a-zA-Z0-9_-]+\.[a-zA-Z0-9_-]+", RegexOptions.Compiled)]
    public static partial Regex JWT();

    [GeneratedRegex(@"-----BEGIN (RSA |EC )?PRIVATE KEY-----", RegexOptions.Compiled)]
    public static partial Regex PrivateKey();
}
