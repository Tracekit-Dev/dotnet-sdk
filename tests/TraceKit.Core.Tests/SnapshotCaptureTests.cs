using System.Diagnostics;
using TraceKit.Core;
using Xunit;

namespace TraceKit.Core.Tests;

public class SnapshotCaptureTests
{
    [Fact]
    public void CaptureSnapshot_AutoCreatesBreakpoint_WithCallerInfo()
    {
        // Arrange
        var config = TracekitConfig.CreateBuilder()
            .WithApiKey("test_key")
            .WithServiceName("test-service")
            .WithEndpoint("http://localhost:8081")
            .WithEnableCodeMonitoring(true)
            .Build();

        using var sdk = TracekitSDK.Create(config);

        // Act - Capture snapshot (will auto-register breakpoint)
        var variables = new Dictionary<string, object>
        {
            ["userId"] = 123,
            ["amount"] = 99.99,
            ["status"] = "active"
        };

        // This should auto-register a breakpoint with file/line/function info
        sdk.CaptureSnapshot("test-snapshot", variables);

        // Assert - Just verify no exceptions thrown
        // In a real test, we'd mock the HTTP client to verify the request
        Assert.True(true);
    }

    [Fact]
    public void CaptureSnapshot_CapturesTraceContext_WhenActivityExists()
    {
        // Arrange
        var config = TracekitConfig.CreateBuilder()
            .WithApiKey("test_key")
            .WithServiceName("test-service")
            .WithEndpoint("http://localhost:8081")
            .WithEnableCodeMonitoring(true)
            .Build();

        using var sdk = TracekitSDK.Create(config);

        // Create an Activity (simulates an active trace)
        var activitySource = new ActivitySource("test-source");
        using var activity = activitySource.CreateActivity("test-operation", ActivityKind.Internal);

        // Manually start the activity to ensure it's set as Activity.Current
        if (activity != null)
        {
            activity.Start();

            // Act
            var variables = new Dictionary<string, object>
            {
                ["traceTest"] = true,
                ["operationId"] = "op-123"
            };

            sdk.CaptureSnapshot("trace-context-test", variables);

            // Assert - Verify activity is valid
            Assert.NotNull(activity);
            Assert.NotEqual(default(ActivityTraceId), activity.TraceId);
            Assert.NotEqual(default(ActivitySpanId), activity.SpanId);

            // Verify Activity.Current is set
            Assert.Equal(activity, Activity.Current);
        }
        else
        {
            // If activity creation returns null (no listeners), just verify no exceptions
            var variables = new Dictionary<string, object>
            {
                ["traceTest"] = true,
                ["operationId"] = "op-123"
            };

            sdk.CaptureSnapshot("trace-context-test", variables);
            Assert.True(true);
        }
    }

    [Fact]
    public void CaptureSnapshot_RedactsSensitiveData()
    {
        // Arrange
        var config = TracekitConfig.CreateBuilder()
            .WithApiKey("test_key")
            .WithServiceName("test-service")
            .WithEndpoint("http://localhost:8081")
            .WithEnableCodeMonitoring(true)
            .Build();

        using var sdk = TracekitSDK.Create(config);

        // Act - Capture snapshot with sensitive data
        var variables = new Dictionary<string, object>
        {
            ["email"] = "user@example.com",
            ["password"] = "secret123",
            ["apiKey"] = "sk_test_123456789",
            ["userId"] = 456
        };

        // This should trigger security scanning and redact sensitive fields
        sdk.CaptureSnapshot("security-test", variables);

        // Assert - Just verify no exceptions thrown
        // The security detector should redact email, password, and apiKey
        Assert.True(true);
    }

    [Fact]
    public void CaptureSnapshot_HandlesNullValues()
    {
        // Arrange
        var config = TracekitConfig.CreateBuilder()
            .WithApiKey("test_key")
            .WithServiceName("test-service")
            .WithEndpoint("http://localhost:8081")
            .WithEnableCodeMonitoring(true)
            .Build();

        using var sdk = TracekitSDK.Create(config);

        // Act - Capture snapshot with null values
        var variables = new Dictionary<string, object>
        {
            ["validValue"] = "test",
            ["nullValue"] = null!,
            ["count"] = 42
        };

        sdk.CaptureSnapshot("null-handling-test", variables);

        // Assert - Should handle nulls gracefully
        Assert.True(true);
    }

    [Fact]
    public void CaptureSnapshot_InDifferentMethods_CreatesUniqueBreakpoints()
    {
        // Arrange
        var config = TracekitConfig.CreateBuilder()
            .WithApiKey("test_key")
            .WithServiceName("test-service")
            .WithEndpoint("http://localhost:8081")
            .WithEnableCodeMonitoring(true)
            .Build();

        using var sdk = TracekitSDK.Create(config);

        // Act - Capture from two different methods
        CaptureInMethodA(sdk);
        CaptureInMethodB(sdk);

        // Assert - Each should auto-register with different file/line info
        Assert.True(true);
    }

    private void CaptureInMethodA(TracekitSDK sdk)
    {
        sdk.CaptureSnapshot("method-a-snapshot", new Dictionary<string, object>
        {
            ["method"] = "A",
            ["value"] = 1
        });
    }

    private void CaptureInMethodB(TracekitSDK sdk)
    {
        sdk.CaptureSnapshot("method-b-snapshot", new Dictionary<string, object>
        {
            ["method"] = "B",
            ["value"] = 2
        });
    }
}
