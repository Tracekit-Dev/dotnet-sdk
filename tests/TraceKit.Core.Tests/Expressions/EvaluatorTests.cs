using System.Text.Json;
using TraceKit.Core.Expressions;
using Xunit;

namespace TraceKit.Core.Tests.Expressions;

public class EvaluatorTests
{
    private static readonly Lazy<FixtureData> _fixture = new(() =>
    {
        // Walk up from bin/Debug/net10.0 to find testdata at solution root
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        string? fixturePath = null;
        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "testdata", "expression_fixtures.json");
            if (File.Exists(candidate))
            {
                fixturePath = candidate;
                break;
            }
        }

        if (fixturePath == null)
            throw new FileNotFoundException("expression_fixtures.json not found walking up from " + dir);

        var json = File.ReadAllText(fixturePath);
        return JsonSerializer.Deserialize<FixtureData>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;
    });

    public static IEnumerable<object[]> SdkEvaluableTestCases()
    {
        var fixture = _fixture.Value;
        foreach (var tc in fixture.TestCases.Where(t => t.Classify == "sdk-evaluable"))
        {
            yield return new object[] { tc.Id, tc.Expression, tc.Description };
        }
    }

    public static IEnumerable<object[]> ServerOnlyTestCases()
    {
        var fixture = _fixture.Value;
        foreach (var tc in fixture.TestCases.Where(t => t.Classify == "server-only"))
        {
            yield return new object[] { tc.Id, tc.Expression, tc.Description };
        }
    }

    [Theory]
    [MemberData(nameof(SdkEvaluableTestCases))]
    public void SdkEvaluable_IsClassifiedCorrectly(string id, string expression, string description)
    {
        Assert.True(Evaluator.IsSDKEvaluable(expression),
            $"[{id}] Expected sdk-evaluable: {description} -- {expression}");
    }

    [Theory]
    [MemberData(nameof(ServerOnlyTestCases))]
    public void ServerOnly_IsClassifiedCorrectly(string id, string expression, string description)
    {
        Assert.False(Evaluator.IsSDKEvaluable(expression),
            $"[{id}] Expected server-only: {description} -- {expression}");
    }

    [Theory]
    [MemberData(nameof(SdkEvaluableTestCases))]
    public void SdkEvaluable_ProducesExpectedResult(string id, string expression, string description)
    {
        var fixture = _fixture.Value;
        var tc = fixture.TestCases.First(t => t.Id == id);
        var env = BuildEnv(tc);

        var result = Evaluator.EvaluateExpression(expression, env);

        AssertExpectedValue(tc.Expected, result, id, expression, description);
    }

    [Fact]
    public void EmptyCondition_ReturnsTrue()
    {
        Assert.True(Evaluator.EvaluateCondition("", new Dictionary<string, object?>()));
    }

    [Fact]
    public void ServerOnlyCondition_ThrowsUnsupported()
    {
        Assert.Throws<UnsupportedExpressionException>(() =>
            Evaluator.EvaluateCondition("len(user.tags) > 1", new Dictionary<string, object?>()));
    }

    [Fact]
    public void EvaluateExpressions_BatchEvaluation()
    {
        var env = BuildDefaultEnv();
        var expressions = new List<string> { "status == 200", "method", "status + 100" };
        var results = Evaluator.EvaluateExpressions(expressions, env);

        Assert.Equal(3, results.Count);
        Assert.Equal(true, results["status == 200"]);
        Assert.Equal("GET", results["method"]);
    }

    private Dictionary<string, object?> BuildEnv(TestCase tc)
    {
        if (tc.Variables != null && tc.Variables.Value.ValueKind != JsonValueKind.Null)
            return ConvertJsonElement(tc.Variables.Value);

        return BuildDefaultEnv();
    }

    private Dictionary<string, object?> BuildDefaultEnv()
    {
        var fixture = _fixture.Value;
        return ConvertJsonElement(fixture.DefaultVariables);
    }

    private Dictionary<string, object?> ConvertJsonElement(JsonElement element)
    {
        var result = new Dictionary<string, object?>();
        foreach (var prop in element.EnumerateObject())
        {
            result[prop.Name] = ConvertValue(prop.Value);
        }
        return result;
    }

    private object? ConvertValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => ConvertJsonElement(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertValue).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? (object)l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => null,
        };
    }

    private static void AssertExpectedValue(JsonElement? expected, object? actual, string id, string expression, string description)
    {
        if (expected == null || expected.Value.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(actual);
            return;
        }

        var exp = expected.Value;

        switch (exp.ValueKind)
        {
            case JsonValueKind.True:
                Assert.True(actual is bool b && b,
                    $"[{id}] Expected true for '{expression}' ({description}), got {actual} ({actual?.GetType().Name})");
                break;
            case JsonValueKind.False:
                Assert.True(actual is bool b2 && !b2,
                    $"[{id}] Expected false for '{expression}' ({description}), got {actual} ({actual?.GetType().Name})");
                break;
            case JsonValueKind.Number:
                if (exp.TryGetInt64(out var expectedLong))
                {
                    var actualNum = Convert.ToDouble(actual);
                    Assert.True(Math.Abs(actualNum - expectedLong) < 0.001,
                        $"[{id}] Expected {expectedLong} for '{expression}' ({description}), got {actual}");
                }
                else
                {
                    var expectedDouble = exp.GetDouble();
                    var actualDouble = Convert.ToDouble(actual);
                    Assert.True(Math.Abs(actualDouble - expectedDouble) < 0.001,
                        $"[{id}] Expected {expectedDouble} for '{expression}' ({description}), got {actual}");
                }
                break;
            case JsonValueKind.String:
                Assert.Equal(exp.GetString(), actual?.ToString());
                break;
            default:
                Assert.Fail($"[{id}] Unexpected expected value kind: {exp.ValueKind}");
                break;
        }
    }
}

public class FixtureData
{
    public string SpecVersion { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("default_variables")]
    public JsonElement DefaultVariables { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("test_cases")]
    public List<TestCase> TestCases { get; set; } = new();
}

public class TestCase
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string Expression { get; set; } = "";
    public JsonElement? Variables { get; set; }
    public JsonElement? Expected { get; set; }
    public string Classify { get; set; } = "";
}
