using System.Text.RegularExpressions;

namespace TraceKit.Core.Security;

/// <summary>
/// Regex patterns for detecting sensitive data in snapshots.
/// 13 standard patterns with typed [REDACTED:type] markers.
/// </summary>
internal static partial class SecurityPatterns
{
    // PII Patterns
    [GeneratedRegex(@"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled)]
    public static partial Regex Email();

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled)]
    public static partial Regex SSN();

    [GeneratedRegex(@"\b\d{4}[- ]?\d{4}[- ]?\d{4}[- ]?\d{4}\b", RegexOptions.Compiled)]
    public static partial Regex CreditCard();

    [GeneratedRegex(@"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b", RegexOptions.Compiled)]
    public static partial Regex Phone();

    // Credential Patterns
    [GeneratedRegex(@"(?:api[_\-]?key|apikey)\s*[:=]\s*['""]?[A-Za-z0-9_\-]{20,}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public static partial Regex APIKey();

    [GeneratedRegex(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled)]
    public static partial Regex AWSKey();

    [GeneratedRegex(@"aws.{0,20}secret.{0,20}[A-Za-z0-9/+=]{40}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public static partial Regex AWSSecret();

    [GeneratedRegex(@"(?:bearer\s+)[A-Za-z0-9._~+/=\-]{20,}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public static partial Regex OAuthToken();

    [GeneratedRegex(@"sk_live_[0-9a-zA-Z]{10,}", RegexOptions.Compiled)]
    public static partial Regex StripeKey();

    [GeneratedRegex(@"(?:password|passwd|pwd)\s*[=:]\s*['""]?[^\s'""]{6,}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public static partial Regex Password();

    [GeneratedRegex(@"eyJ[a-zA-Z0-9_\-]+\.eyJ[a-zA-Z0-9_\-]+\.[a-zA-Z0-9_\-]+", RegexOptions.Compiled)]
    public static partial Regex JWT();

    [GeneratedRegex(@"-----BEGIN (?:RSA |EC )?PRIVATE KEY-----", RegexOptions.Compiled)]
    public static partial Regex PrivateKey();

    // Letter-boundary pattern -- \b treats _ as word char, so api_key/user_token won't match
    [GeneratedRegex(@"(?:^|[^a-zA-Z])(?:password|passwd|pwd|secret|token|key|credential|api_key|apikey)(?:[^a-zA-Z]|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    public static partial Regex SensitiveName();
}
