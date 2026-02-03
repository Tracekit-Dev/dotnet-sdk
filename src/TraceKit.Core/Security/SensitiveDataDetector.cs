using System.Text.Json;

namespace TraceKit.Core.Security;

/// <summary>
/// Detects and redacts sensitive data (PII, credentials) from variable snapshots.
/// </summary>
public sealed class SensitiveDataDetector
{
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
        if (value == null) return (value, new List<SecurityFlag>());

        var flags = new List<SecurityFlag>();
        var valueStr = value.ToString() ?? "";

        // Check for PII
        if (SecurityPatterns.Email().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("pii", "email", "medium", key, true));
            return ("[REDACTED]", flags);
        }

        if (SecurityPatterns.SSN().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("pii", "ssn", "critical", key, true));
            return ("[REDACTED]", flags);
        }

        if (SecurityPatterns.CreditCard().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("pii", "credit_card", "critical", key, true));
            return ("[REDACTED]", flags);
        }

        if (SecurityPatterns.Phone().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("pii", "phone", "medium", key, true));
            return ("[REDACTED]", flags);
        }

        // Check for credentials
        if (SecurityPatterns.APIKey().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("credential", "api_key", "critical", key, true));
            return ("[REDACTED]", flags);
        }

        if (SecurityPatterns.AWSKey().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("credential", "aws_key", "critical", key, true));
            return ("[REDACTED]", flags);
        }

        if (SecurityPatterns.StripeKey().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("credential", "stripe_key", "critical", key, true));
            return ("[REDACTED]", flags);
        }

        if (SecurityPatterns.Password().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("credential", "password", "critical", key, true));
            return ("[REDACTED]", flags);
        }

        if (SecurityPatterns.JWT().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("credential", "jwt", "high", key, true));
            return ("[REDACTED]", flags);
        }

        if (SecurityPatterns.PrivateKey().IsMatch(valueStr))
        {
            flags.Add(new SecurityFlag("credential", "private_key", "critical", key, true));
            return ("[REDACTED]", flags);
        }

        return (value, flags);
    }
}
