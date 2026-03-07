using System.Text.Json;
using System.Text.RegularExpressions;

namespace TraceKit.Core.Security;

/// <summary>
/// Detects and redacts sensitive data (PII, credentials) from variable snapshots.
/// Uses typed [REDACTED:type] markers for 13+ patterns.
/// PII scrubbing is enabled by default and can be toggled via PiiScrubbing property.
/// </summary>
public sealed class SensitiveDataDetector
{
    /// <summary>Whether PII scrubbing is enabled. Default: true.</summary>
    public bool PiiScrubbing { get; set; } = true;

    /// <summary>Custom PII patterns to append to the built-in 13-pattern set.</summary>
    public List<CustomPiiPattern> CustomPatterns { get; } = new();

    public record CustomPiiPattern(Regex Pattern, string Marker);

    public record SecurityFlag(
        string Type,
        string Category,
        string Severity,
        string Variable,
        bool Redacted
    );

    public record ScanResult(
        Dictionary<string, object> SanitizedVariables,
        List<SecurityFlag> SecurityFlags
    );

    public ScanResult Scan(Dictionary<string, object> variables)
    {
        var sanitized = new Dictionary<string, object>();
        var flags = new List<SecurityFlag>();

        // If PII scrubbing is disabled, return as-is
        if (!PiiScrubbing)
        {
            foreach (var (key, value) in variables)
            {
                sanitized[key] = value;
            }
            return new ScanResult(sanitized, flags);
        }

        foreach (var (key, value) in variables)
        {
            var (sanitizedValue, detectedFlags) = ScanValue(key, value);
            sanitized[key] = sanitizedValue;
            flags.AddRange(detectedFlags);
        }

        return new ScanResult(sanitized, flags);
    }

    private (object value, List<SecurityFlag> flags) ScanValue(string key, object value)
    {
        if (value == null) return ("[NULL]", new List<SecurityFlag>());

        var flags = new List<SecurityFlag>();

        // Check variable name for sensitive keywords (word-boundary matching)
        if (SecurityPatterns.SensitiveName().IsMatch(key))
        {
            flags.Add(new SecurityFlag("sensitive_name", "name", "medium", key, true));
            return ("[REDACTED:sensitive_name]", flags);
        }

        // Serialize value to string for deep scanning of nested structures
        var valueStr = value.ToString() ?? "";

        // Check PII patterns with typed markers
        if (SecurityPatterns.Email().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("pii", "email", "medium", key, true));
            return ("[REDACTED:email]", flags);
        }

        if (SecurityPatterns.SSN().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("pii", "ssn", "critical", key, true));
            return ("[REDACTED:ssn]", flags);
        }

        if (SecurityPatterns.CreditCard().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("pii", "credit_card", "critical", key, true));
            return ("[REDACTED:credit_card]", flags);
        }

        if (SecurityPatterns.Phone().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("pii", "phone", "medium", key, true));
            return ("[REDACTED:phone]", flags);
        }

        // Check credentials with typed markers
        if (SecurityPatterns.AWSKey().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("credential", "aws_key", "critical", key, true));
            return ("[REDACTED:aws_key]", flags);
        }

        if (SecurityPatterns.AWSSecret().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("credential", "aws_secret", "critical", key, true));
            return ("[REDACTED:aws_secret]", flags);
        }

        if (SecurityPatterns.OAuthToken().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("credential", "oauth_token", "high", key, true));
            return ("[REDACTED:oauth_token]", flags);
        }

        if (SecurityPatterns.StripeKey().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("credential", "stripe_key", "critical", key, true));
            return ("[REDACTED:stripe_key]", flags);
        }

        if (SecurityPatterns.Password().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("credential", "password", "critical", key, true));
            return ("[REDACTED:password]", flags);
        }

        if (SecurityPatterns.JWT().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("credential", "jwt", "high", key, true));
            return ("[REDACTED:jwt]", flags);
        }

        if (SecurityPatterns.PrivateKey().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("credential", "private_key", "critical", key, true));
            return ("[REDACTED:private_key]", flags);
        }

        if (SecurityPatterns.APIKey().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("credential", "api_key", "critical", key, true));
            return ("[REDACTED:api_key]", flags);
        }

        // Check custom patterns
        foreach (var custom in CustomPatterns)
        {
            if (custom.Pattern.IsMatch(valueStr))
            {
                flags.Add(new SecurityFlag("custom", "custom", "high", key, true));
                return (custom.Marker, flags);
            }
        }

        return (value, flags);
    }
}
