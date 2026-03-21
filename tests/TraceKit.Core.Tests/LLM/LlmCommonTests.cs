using TraceKit.Core.LLM;
using Xunit;

namespace TraceKit.Core.Tests.LLM;

public class LlmCommonTests
{
    // --- PII Scrubbing Tests ---

    [Fact]
    public void ScrubPii_RemovesEmail()
    {
        var result = LlmCommon.ScrubPii("Contact me at user@example.com");
        Assert.DoesNotContain("user@example.com", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void ScrubPii_RemovesSSN()
    {
        var result = LlmCommon.ScrubPii("SSN: 123-45-6789");
        Assert.DoesNotContain("123-45-6789", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void ScrubPii_RemovesCreditCard()
    {
        var result = LlmCommon.ScrubPii("Card: 4111-1111-1111-1111");
        Assert.DoesNotContain("4111-1111-1111-1111", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void ScrubPii_RemovesAwsKey()
    {
        var result = LlmCommon.ScrubPii("Key: AKIAIOSFODNN7EXAMPLE");
        Assert.DoesNotContain("AKIAIOSFODNN7EXAMPLE", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void ScrubPii_RemovesStripeKey()
    {
        var result = LlmCommon.ScrubPii("sk_live_abc123def456ghi");
        Assert.DoesNotContain("sk_live_abc123def456ghi", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void ScrubPii_RemovesJwt()
    {
        var jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";
        var result = LlmCommon.ScrubPii(jwt);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void ScrubPii_RemovesPrivateKeyHeader()
    {
        var result = LlmCommon.ScrubPii("-----BEGIN RSA PRIVATE KEY-----");
        Assert.DoesNotContain("BEGIN RSA PRIVATE KEY", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public void ScrubPii_LeavesCleanContentUnchanged()
    {
        var input = "The weather is nice";
        var result = LlmCommon.ScrubPii(input);
        Assert.Equal(input, result);
    }

    // --- JSON Key Scrubbing Tests ---

    [Fact]
    public void ScrubJsonKeys_RedactsSensitiveKeys()
    {
        var json = """{"password":"secret","user":"alice"}""";
        var result = LlmCommon.ScrubJsonKeys(json);
        Assert.DoesNotContain("secret", result);
        Assert.Contains("[REDACTED]", result);
        Assert.Contains("alice", result);
    }

    // --- Provider Detection Tests ---

    [Fact]
    public void DetectProvider_OpenAI()
    {
        Assert.Equal("openai", LlmCommon.DetectProvider("api.openai.com"));
    }

    [Fact]
    public void DetectProvider_Anthropic()
    {
        Assert.Equal("anthropic", LlmCommon.DetectProvider("api.anthropic.com"));
    }

    [Fact]
    public void DetectProvider_Unknown()
    {
        Assert.Null(LlmCommon.DetectProvider("example.com"));
    }

    [Fact]
    public void DetectProvider_StripPort()
    {
        Assert.Equal("openai", LlmCommon.DetectProvider("api.openai.com:443"));
    }

    // --- ShouldCaptureContent Tests ---

    [Fact]
    public void ShouldCaptureContent_DefaultIsFalse()
    {
        try
        {
            Environment.SetEnvironmentVariable("TRACEKIT_LLM_CAPTURE_CONTENT", null);
            var config = LlmConfig.Default;
            Assert.False(LlmCommon.ShouldCaptureContent(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRACEKIT_LLM_CAPTURE_CONTENT", null);
        }
    }

    [Fact]
    public void ShouldCaptureContent_EnvVarTrue()
    {
        try
        {
            Environment.SetEnvironmentVariable("TRACEKIT_LLM_CAPTURE_CONTENT", "true");
            var config = LlmConfig.Default;
            Assert.True(LlmCommon.ShouldCaptureContent(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRACEKIT_LLM_CAPTURE_CONTENT", null);
        }
    }

    [Fact]
    public void ShouldCaptureContent_EnvVarOne()
    {
        try
        {
            Environment.SetEnvironmentVariable("TRACEKIT_LLM_CAPTURE_CONTENT", "1");
            var config = LlmConfig.Default;
            Assert.True(LlmCommon.ShouldCaptureContent(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRACEKIT_LLM_CAPTURE_CONTENT", null);
        }
    }

    [Fact]
    public void ShouldCaptureContent_ConfigOverride()
    {
        try
        {
            Environment.SetEnvironmentVariable("TRACEKIT_LLM_CAPTURE_CONTENT", null);
            var config = new LlmConfig { CaptureContent = true };
            Assert.True(LlmCommon.ShouldCaptureContent(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRACEKIT_LLM_CAPTURE_CONTENT", null);
        }
    }

    // --- LlmConfig Defaults Tests ---

    [Fact]
    public void LlmConfig_Defaults()
    {
        var config = LlmConfig.Default;
        Assert.True(config.Enabled);
        Assert.True(config.OpenAI);
        Assert.True(config.Anthropic);
        Assert.False(config.CaptureContent);
    }
}
