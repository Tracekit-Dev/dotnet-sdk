using System.Diagnostics;
using System.Net;
using System.Text;
using TraceKit.Core.LLM;
using Xunit;

namespace TraceKit.Core.Tests.LLM;

/// <summary>
/// Tests for AnthropicHandler integration via TracekitLlmHandler.
/// Uses MockInnerHandler + ActivityListener pattern matching OpenAiHandlerTests.
/// </summary>
public class AnthropicHandlerTests : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = new();

    public AnthropicHandlerTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "tracekit-llm",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => _activities.Add(activity),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        Environment.SetEnvironmentVariable("TRACEKIT_LLM_CAPTURE_CONTENT", null);
    }

    // --- Non-streaming Response ---

    [Fact]
    public async Task NonStreamingResponse_SetsGenAiAttributes()
    {
        var responseJson = """
        {
            "id": "msg_01XFDUDYJgAACzvnptvVoYEL",
            "type": "message",
            "model": "claude-sonnet-4-20250514",
            "content": [
                {"type": "text", "text": "Hello!"}
            ],
            "stop_reason": "end_turn",
            "usage": {
                "input_tokens": 25,
                "output_tokens": 10
            }
        }
        """;

        var mock = new MockInnerHandler(CreateJsonResponse(responseJson));
        var handler = new TracekitLlmHandler(LlmConfig.Default, mock);
        var client = new HttpClient(handler);

        await client.PostAsync("https://api.anthropic.com/v1/messages",
            new StringContent(AnthropicRequestJson(), Encoding.UTF8, "application/json"));

        Assert.Single(_activities);
        var activity = _activities[0];

        Assert.Equal("chat claude-sonnet-4-20250514", activity.DisplayName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal("chat", activity.GetTagItem("gen_ai.operation.name")?.ToString());
        Assert.Equal("anthropic", activity.GetTagItem("gen_ai.system")?.ToString());
        Assert.Equal("claude-sonnet-4-20250514", activity.GetTagItem("gen_ai.request.model")?.ToString());
        Assert.Equal("claude-sonnet-4-20250514", activity.GetTagItem("gen_ai.response.model")?.ToString());
        Assert.Equal("msg_01XFDUDYJgAACzvnptvVoYEL", activity.GetTagItem("gen_ai.response.id")?.ToString());
        Assert.Equal("end_turn", activity.GetTagItem("gen_ai.response.finish_reasons")?.ToString());
        Assert.Equal(25, activity.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(10, activity.GetTagItem("gen_ai.usage.output_tokens"));
    }

    // --- Streaming Response ---

    [Fact]
    public async Task StreamingResponse_AccumulatesTokens()
    {
        var sseData = new StringBuilder();
        // Anthropic SSE uses event:/data: line pairs
        sseData.AppendLine("event: message_start");
        sseData.AppendLine("""data: {"type":"message_start","message":{"id":"msg_stream1","type":"message","model":"claude-sonnet-4-20250514","content":[],"stop_reason":null,"usage":{"input_tokens":20,"output_tokens":0}}}""");
        sseData.AppendLine();
        sseData.AppendLine("event: content_block_start");
        sseData.AppendLine("""data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""");
        sseData.AppendLine();
        sseData.AppendLine("event: content_block_delta");
        sseData.AppendLine("""data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hi"}}""");
        sseData.AppendLine();
        sseData.AppendLine("event: message_delta");
        sseData.AppendLine("""data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":8}}""");
        sseData.AppendLine();
        sseData.AppendLine("event: message_stop");
        sseData.AppendLine("""data: {"type":"message_stop"}""");
        sseData.AppendLine();

        var streamRequestJson = """
        {
            "model": "claude-sonnet-4-20250514",
            "messages": [{"role": "user", "content": "Hi"}],
            "max_tokens": 1024,
            "stream": true
        }
        """;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(
                new MemoryStream(Encoding.UTF8.GetBytes(sseData.ToString())))
        };
        response.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");

        var mock = new MockInnerHandler(response);
        var handler = new TracekitLlmHandler(LlmConfig.Default, mock);
        var client = new HttpClient(handler);

        var result = await client.PostAsync("https://api.anthropic.com/v1/messages",
            new StringContent(streamRequestJson, Encoding.UTF8, "application/json"));

        // Consume the stream to trigger SSE parsing
        var outputStream = new MemoryStream();
        await result.Content.CopyToAsync(outputStream);

        Assert.Single(_activities);
        var activity = _activities[0];

        Assert.Equal("chat claude-sonnet-4-20250514", activity.DisplayName);
        Assert.Equal("msg_stream1", activity.GetTagItem("gen_ai.response.id")?.ToString());
        Assert.Equal("claude-sonnet-4-20250514", activity.GetTagItem("gen_ai.response.model")?.ToString());
        Assert.Equal("end_turn", activity.GetTagItem("gen_ai.response.finish_reasons")?.ToString());
        Assert.Equal(20, activity.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(8, activity.GetTagItem("gen_ai.usage.output_tokens"));
    }

    // --- Tool Calls ---

    [Fact]
    public async Task ToolUseBlocks_RecordedAsEvents()
    {
        var responseJson = """
        {
            "id": "msg_tools",
            "type": "message",
            "model": "claude-sonnet-4-20250514",
            "content": [
                {
                    "type": "tool_use",
                    "id": "toolu_01A09q90qw90lq917835lq9",
                    "name": "get_weather",
                    "input": {"city": "London"}
                }
            ],
            "stop_reason": "tool_use",
            "usage": { "input_tokens": 30, "output_tokens": 15 }
        }
        """;

        var mock = new MockInnerHandler(CreateJsonResponse(responseJson));
        var handler = new TracekitLlmHandler(LlmConfig.Default, mock);
        var client = new HttpClient(handler);

        await client.PostAsync("https://api.anthropic.com/v1/messages",
            new StringContent(AnthropicRequestJson(), Encoding.UTF8, "application/json"));

        Assert.Single(_activities);
        var activity = _activities[0];

        Assert.Equal("tool_use", activity.GetTagItem("gen_ai.response.finish_reasons")?.ToString());

        var events = activity.Events.ToList();
        Assert.Single(events);
        Assert.Equal("gen_ai.tool.call", events[0].Name);

        var tags = events[0].Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("get_weather", tags["gen_ai.tool.name"]);
        Assert.Equal("toolu_01A09q90qw90lq917835lq9", tags["gen_ai.tool.call.id"]);
        Assert.Contains("London", tags["gen_ai.tool.call.arguments"]?.ToString());
    }

    // --- Content Capture with System Prompt ---

    [Fact]
    public async Task ContentCapture_WhenEnabled_SetsInputOutputAndSystemInstructions()
    {
        try
        {
            Environment.SetEnvironmentVariable("TRACEKIT_LLM_CAPTURE_CONTENT", "true");

            var responseJson = """
            {
                "id": "msg_cap",
                "type": "message",
                "model": "claude-sonnet-4-20250514",
                "content": [{"type": "text", "text": "I am helpful."}],
                "stop_reason": "end_turn",
                "usage": { "input_tokens": 15, "output_tokens": 5 }
            }
            """;

            var requestJson = """
            {
                "model": "claude-sonnet-4-20250514",
                "messages": [{"role": "user", "content": "Hello"}],
                "system": "You are a helpful assistant.",
                "max_tokens": 1024
            }
            """;

            var mock = new MockInnerHandler(CreateJsonResponse(responseJson));
            var handler = new TracekitLlmHandler(LlmConfig.Default, mock);
            var client = new HttpClient(handler);

            await client.PostAsync("https://api.anthropic.com/v1/messages",
                new StringContent(requestJson, Encoding.UTF8, "application/json"));

            Assert.Single(_activities);
            var activity = _activities[0];

            Assert.NotNull(activity.GetTagItem("gen_ai.input.messages"));
            Assert.NotNull(activity.GetTagItem("gen_ai.output.messages"));
            Assert.NotNull(activity.GetTagItem("gen_ai.system_instructions"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TRACEKIT_LLM_CAPTURE_CONTENT", null);
        }
    }

    // --- Cache Tokens ---

    [Fact]
    public async Task CacheTokens_SetWhenPresent()
    {
        var responseJson = """
        {
            "id": "msg_cache",
            "type": "message",
            "model": "claude-sonnet-4-20250514",
            "content": [{"type": "text", "text": "Cached response"}],
            "stop_reason": "end_turn",
            "usage": {
                "input_tokens": 50,
                "output_tokens": 10,
                "cache_creation_input_tokens": 100,
                "cache_read_input_tokens": 200
            }
        }
        """;

        var mock = new MockInnerHandler(CreateJsonResponse(responseJson));
        var handler = new TracekitLlmHandler(LlmConfig.Default, mock);
        var client = new HttpClient(handler);

        await client.PostAsync("https://api.anthropic.com/v1/messages",
            new StringContent(AnthropicRequestJson(), Encoding.UTF8, "application/json"));

        Assert.Single(_activities);
        var activity = _activities[0];

        Assert.Equal(100, activity.GetTagItem("gen_ai.usage.cache_creation.input_tokens"));
        Assert.Equal(200, activity.GetTagItem("gen_ai.usage.cache_read.input_tokens"));
    }

    // --- Streaming with Cache Tokens ---

    [Fact]
    public async Task StreamingResponse_CacheTokensFromMessageStart()
    {
        var sseData = new StringBuilder();
        sseData.AppendLine("event: message_start");
        sseData.AppendLine("""data: {"type":"message_start","message":{"id":"msg_scache","type":"message","model":"claude-sonnet-4-20250514","content":[],"stop_reason":null,"usage":{"input_tokens":10,"output_tokens":0,"cache_creation_input_tokens":50,"cache_read_input_tokens":75}}}""");
        sseData.AppendLine();
        sseData.AppendLine("event: message_delta");
        sseData.AppendLine("""data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":5}}""");
        sseData.AppendLine();
        sseData.AppendLine("event: message_stop");
        sseData.AppendLine("""data: {"type":"message_stop"}""");
        sseData.AppendLine();

        var streamRequestJson = """
        {
            "model": "claude-sonnet-4-20250514",
            "messages": [{"role": "user", "content": "test"}],
            "max_tokens": 1024,
            "stream": true
        }
        """;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(
                new MemoryStream(Encoding.UTF8.GetBytes(sseData.ToString())))
        };
        response.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");

        var mock = new MockInnerHandler(response);
        var handler = new TracekitLlmHandler(LlmConfig.Default, mock);
        var client = new HttpClient(handler);

        var result = await client.PostAsync("https://api.anthropic.com/v1/messages",
            new StringContent(streamRequestJson, Encoding.UTF8, "application/json"));

        await result.Content.CopyToAsync(new MemoryStream());

        Assert.Single(_activities);
        var activity = _activities[0];

        Assert.Equal(50, activity.GetTagItem("gen_ai.usage.cache_creation.input_tokens"));
        Assert.Equal(75, activity.GetTagItem("gen_ai.usage.cache_read.input_tokens"));
        Assert.Equal(10, activity.GetTagItem("gen_ai.usage.input_tokens"));
        Assert.Equal(5, activity.GetTagItem("gen_ai.usage.output_tokens"));
    }

    // --- Error Status Code ---

    [Fact]
    public async Task ErrorStatusCode_SetsHttpStatusCodeAttribute()
    {
        var mock = new MockInnerHandler(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var handler = new TracekitLlmHandler(LlmConfig.Default, mock);
        var client = new HttpClient(handler);

        await client.PostAsync("https://api.anthropic.com/v1/messages",
            new StringContent(AnthropicRequestJson(), Encoding.UTF8, "application/json"));

        Assert.Single(_activities);
        var activity = _activities[0];
        Assert.Equal(429, activity.GetTagItem("http.status_code"));
    }

    // --- Unparseable Body ---

    [Fact]
    public async Task UnparseableBody_PassesThroughWithoutInstrumentation()
    {
        var mock = new MockInnerHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new TracekitLlmHandler(LlmConfig.Default, mock);
        var client = new HttpClient(handler);

        await client.PostAsync("https://api.anthropic.com/v1/messages",
            new StringContent("this is not json", Encoding.UTF8, "application/json"));

        Assert.Empty(_activities);
    }

    // --- Helpers ---

    private static string AnthropicRequestJson() => """
        {
            "model": "claude-sonnet-4-20250514",
            "messages": [
                {"role": "user", "content": "Hello"}
            ],
            "max_tokens": 1024
        }
        """;

    private static HttpResponseMessage CreateJsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    /// <summary>
    /// Mock DelegatingHandler inner handler for testing.
    /// </summary>
    private class MockInnerHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockInnerHandler(HttpResponseMessage response)
        {
            _handler = _ => response;
        }

        public MockInnerHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
