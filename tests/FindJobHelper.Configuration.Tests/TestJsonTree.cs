using System.Text.Json;
using System.Text.Json.Nodes;

namespace FindJobHelper.Configuration.Tests;

internal sealed class TestJsonTree
{
    private readonly JsonObject _root;

    private TestJsonTree(JsonObject root)
    {
        _root = root;
    }

    public static TestJsonTree Parse(string json)
    {
        var root = ParseNode(json)?.AsObject()
            ?? throw new InvalidOperationException(
                "The test JSON must contain an object at its root.");
        return new(root);
    }

    public TestJsonTree Set(string path, JsonNode? value)
    {
        var (parent, propertyName) = ResolveParent(path);
        parent[propertyName] = value;
        return this;
    }

    public TestJsonTree SetJson(string path, string json)
    {
        return Set(path, ParseNode(json));
    }

    public TestJsonTree Remove(string path)
    {
        var (parent, propertyName) = ResolveParent(path);
        parent.Remove(propertyName);
        return this;
    }

    public JsonArray Array(string path) => Node(path).AsArray();

    public string ToJsonString() => _root.ToJsonString();

    private static JsonNode? ParseNode(string json)
    {
        return JsonNode.Parse(
            json,
            documentOptions: new()
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
    }

    private JsonNode Node(string path)
    {
        var (parent, propertyName) = ResolveParent(path);
        return parent[propertyName]
            ?? throw new InvalidOperationException(
                $"Test JSON path '{path}' does not contain a value.");
    }

    private (JsonObject Parent, string PropertyName) ResolveParent(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var segments = path.Split('.');
        var current = _root;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            var segment = segments[index];
            if (!current.TryGetPropertyValue(segment, out var child))
            {
                var created = new JsonObject();
                current[segment] = created;
                current = created;
                continue;
            }
            if (child is null)
            {
                var created = new JsonObject();
                current[segment] = created;
                current = created;
                continue;
            }

            current = child as JsonObject
                ?? throw new InvalidOperationException(
                    $"Test JSON path '{string.Join('.', segments.Take(index + 1))}' " +
                    "does not contain an object.");
        }

        return (current, segments[^1]);
    }
}
