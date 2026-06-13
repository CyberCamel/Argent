using Argent.Runtime.Workflows.Execution;
using Xunit;

namespace Argent.Runtime.Tests.Workflows.Execution;

public class TokenMovementTests
{
    [Fact]
    public void MergeVariables_returns_current_when_delta_null()
    {
        var current = new Dictionary<string, object?> { ["a"] = "1" };
        var result = TokenMovement.MergeVariables(current, null);
        Assert.Single(result);
        Assert.Equal("1", result["a"]);
    }

    [Fact]
    public void MergeVariables_returns_current_when_delta_empty()
    {
        var current = new Dictionary<string, object?> { ["a"] = "1" };
        var result = TokenMovement.MergeVariables(current, new Dictionary<string, object?>());
        Assert.Single(result);
        Assert.Equal("1", result["a"]);
    }

    [Fact]
    public void MergeVariables_overlays_delta_on_current()
    {
        var current = new Dictionary<string, object?> { ["a"] = "1", ["b"] = "2" };
        var delta = new Dictionary<string, object?> { ["b"] = "3", ["c"] = "4" };
        var result = TokenMovement.MergeVariables(current, delta);
        Assert.Equal("1", result["a"]);
        Assert.Equal("3", result["b"]);
        Assert.Equal("4", result["c"]);
    }

    [Fact]
    public void SerializePayload_returns_empty_json_for_null()
    {
        Assert.Equal("{}", TokenMovement.SerializePayload(null));
    }

    [Fact]
    public void SerializePayload_returns_empty_json_for_empty()
    {
        Assert.Equal("{}", TokenMovement.SerializePayload(new Dictionary<string, object?>()));
    }

    [Fact]
    public void SerializePayload_serializes_variables()
    {
        var vars = new Dictionary<string, object?> { ["key"] = "value", ["num"] = 42 };
        var json = TokenMovement.SerializePayload(vars);
        Assert.Contains("\"key\":\"value\"", json);
        Assert.Contains("\"num\":42", json);
    }

    [Fact]
    public void DeserializePayload_returns_empty_for_null()
    {
        var result = TokenMovement.DeserializePayload(null);
        Assert.Empty(result);
    }

    [Fact]
    public void DeserializePayload_returns_empty_for_whitespace()
    {
        var result = TokenMovement.DeserializePayload("   ");
        Assert.Empty(result);
    }

    [Fact]
    public void DeserializePayload_returns_empty_for_invalid_json()
    {
        var result = TokenMovement.DeserializePayload("not-json");
        Assert.Empty(result);
    }

    [Fact]
    public void DeserializePayload_deserializes_valid_json()
    {
        var result = TokenMovement.DeserializePayload(@"{""key"":""value"",""num"":42}");
        Assert.Equal("value", result["key"]?.ToString());
    }

    [Fact]
    public void Roundtrip_serialize_deserialize()
    {
        var original = new Dictionary<string, object?> { ["name"] = "test", ["count"] = 10 };
        var json = TokenMovement.SerializePayload(original);
        var result = TokenMovement.DeserializePayload(json);
        Assert.Equal("test", result["name"]?.ToString());
    }
}
