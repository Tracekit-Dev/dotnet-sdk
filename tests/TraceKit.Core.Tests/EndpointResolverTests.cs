using TraceKit.Core;
using Xunit;

namespace TraceKit.Core.Tests;

public class EndpointResolverTests
{
    [Fact]
    public void ResolveEndpoint_WithHostOnly_ReturnsHttpsURL()
    {
        var result = EndpointResolver.ResolveEndpoint("app.tracekit.dev", "/v1/traces", useSSL: true);
        Assert.Equal("https://app.tracekit.dev/v1/traces", result);
    }

    [Fact]
    public void ResolveEndpoint_WithHostOnly_ReturnsHttpURL()
    {
        var result = EndpointResolver.ResolveEndpoint("localhost:8081", "/v1/traces", useSSL: false);
        Assert.Equal("http://localhost:8081/v1/traces", result);
    }

    [Fact]
    public void ResolveEndpoint_WithTrailingSlash_RemovesIt()
    {
        var result = EndpointResolver.ResolveEndpoint("app.tracekit.dev/", "/v1/metrics", useSSL: true);
        Assert.Equal("https://app.tracekit.dev/v1/metrics", result);
    }

    [Fact]
    public void ResolveEndpoint_WithHttpScheme_IgnoresSSLFlag()
    {
        var result = EndpointResolver.ResolveEndpoint("http://localhost:8081", "/v1/traces", useSSL: true);
        Assert.Equal("http://localhost:8081/v1/traces", result);
    }

    [Fact]
    public void ResolveEndpoint_WithHttpsScheme_IgnoresSSLFlag()
    {
        var result = EndpointResolver.ResolveEndpoint("https://app.tracekit.dev", "/v1/metrics", useSSL: false);
        Assert.Equal("https://app.tracekit.dev/v1/metrics", result);
    }

    [Fact]
    public void ResolveEndpoint_WithSchemeAndTrailingSlash_RemovesSlash()
    {
        var result = EndpointResolver.ResolveEndpoint("http://localhost:8081/", "/v1/traces", useSSL: true);
        Assert.Equal("http://localhost:8081/v1/traces", result);
    }

    [Fact]
    public void ResolveEndpoint_WithFullURL_ExtractsBaseAndAppendsNewPath()
    {
        var result = EndpointResolver.ResolveEndpoint("http://localhost:8081/v1/traces", "/v1/traces", useSSL: true);
        Assert.Equal("http://localhost:8081/v1/traces", result);
    }

    [Fact]
    public void ResolveEndpoint_WithCustomBasePath_ExtractsBaseAndAppendsNewPath()
    {
        var result = EndpointResolver.ResolveEndpoint("http://localhost:8081/custom/path", "/v1/traces", useSSL: true);
        Assert.Equal("http://localhost:8081/v1/traces", result);
    }

    [Fact]
    public void ResolveEndpoint_WithComplexPath_ExtractsBaseAndAppendsNewPath()
    {
        var result = EndpointResolver.ResolveEndpoint("https://app.tracekit.dev/api/v2/", "/v1/traces", useSSL: false);
        Assert.Equal("https://app.tracekit.dev/v1/traces", result);
    }

    [Fact]
    public void ResolveEndpoint_WithEmptyPath_ReturnsBaseOnly()
    {
        var result = EndpointResolver.ResolveEndpoint("app.tracekit.dev", "", useSSL: true);
        Assert.Equal("https://app.tracekit.dev", result);
    }

    [Fact]
    public void ResolveEndpoint_WithSchemeAndEmptyPath_ReturnsBase()
    {
        var result = EndpointResolver.ResolveEndpoint("http://localhost:8081", "", useSSL: true);
        Assert.Equal("http://localhost:8081", result);
    }

    [Fact]
    public void ResolveEndpoint_WithFullURLAndEmptyPath_ExtractsBase()
    {
        var result = EndpointResolver.ResolveEndpoint("http://localhost:8081/v1/traces", "", useSSL: true);
        Assert.Equal("http://localhost:8081", result);
    }

    [Fact]
    public void ExtractBaseURL_FromFullURL_ReturnsSchemeAndHost()
    {
        var result = EndpointResolver.ExtractBaseURL("https://app.tracekit.dev/v1/traces");
        Assert.Equal("https://app.tracekit.dev", result);
    }

    [Fact]
    public void ExtractBaseURL_FromLocalhost_ReturnsSchemeAndHost()
    {
        var result = EndpointResolver.ExtractBaseURL("http://localhost:8081/custom/path");
        Assert.Equal("http://localhost:8081", result);
    }
}
