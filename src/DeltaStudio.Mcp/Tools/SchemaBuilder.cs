using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeltaStudio.Mcp.Tools;

/// <summary>Small helper to build JSON schemas without dragging a schema library in.</summary>
public static class SchemaBuilder
{
    /// <summary>A property definition: name + schema + required.</summary>
    public readonly record struct Prop(string Name, JsonObject Schema, bool Required = false);

    /// <summary>String property.</summary>
    public static Prop String(string name, string desc, bool required = false) =>
        new(name, new JsonObject { ["type"] = "string", ["description"] = desc }, required);

    /// <summary>Integer property.</summary>
    public static Prop Int(string name, string desc, bool required = false) =>
        new(name, new JsonObject { ["type"] = "integer", ["description"] = desc }, required);

    /// <summary>Boolean property.</summary>
    public static Prop Bool(string name, string desc, bool required = false) =>
        new(name, new JsonObject { ["type"] = "boolean", ["description"] = desc }, required);

    /// <summary>Enum property.</summary>
    public static Prop Enum(string name, string desc, bool required, params string[] values)
    {
        var arr = new JsonArray();
        foreach (string v in values)
        {
            arr.Add(v);
        }

        return new Prop(name, new JsonObject { ["type"] = "string", ["description"] = desc, ["enum"] = arr }, required);
    }

    /// <summary>Array-of-string property.</summary>
    public static Prop StringArray(string name, string desc, bool required = false) =>
        new(name, new JsonObject
        {
            ["type"] = "array",
            ["description"] = desc,
            ["items"] = new JsonObject { ["type"] = "string" },
        }, required);

    /// <summary>JSON text property: callers pass serialized JSON as a string (kept simple + schema-valid).</summary>
    public static Prop Json(string name, string desc, bool required = false) =>
        new(name, new JsonObject { ["type"] = "string", ["description"] = desc + " (JSON encoded as a string)" }, required);

    /// <summary>Builds the top-level object schema.</summary>
    public static JsonObject Object(string desc, params Prop[] props)
    {
        var o = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(),
            ["required"] = new JsonArray(),
        };

        var p = (JsonObject)o["properties"]!;
        var r = (JsonArray)o["required"]!;
        foreach (Prop prop in props)
        {
            p[prop.Name] = prop.Schema;
            if (prop.Required)
            {
                r.Add(prop.Name);
            }
        }

        if (r.Count == 0)
        {
            o.Remove("required");
        }

        o["description"] = desc;
        return o;
    }

    /// <summary>Helper to fetch a string argument with a default.</summary>
    public static string? Str(JsonObject? args, string name, string? fallback = null) =>
        args is not null && args.TryGetPropertyValue(name, out JsonNode? n) && n is JsonValue v && v.TryGetValue(out string? s)
            ? s
            : fallback;

    /// <summary>Helper to fetch an int argument (accepts any JSON number or numeric string).</summary>
    public static int? IntArg(JsonObject? args, string name, int? fallback = null)
    {
        if (args is null || !args.TryGetPropertyValue(name, out JsonNode? n) || n is null)
        {
            return fallback;
        }

        string text = n.ToJsonString().Trim();
        if (int.TryParse(text, out int v))
        {
            return v;
        }

        if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d))
        {
            return (int)d;
        }

        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"'
            && int.TryParse(text[1..^1], out int sv))
        {
            return sv;
        }

        return fallback;
    }

    /// <summary>Helper to fetch a bool argument.</summary>
    public static bool BoolArg(JsonObject? args, string name, bool fallback = false)
    {
        if (args is null || !args.TryGetPropertyValue(name, out JsonNode? n) || n is null)
        {
            return fallback;
        }

        try
        {
            return n.GetValue<bool>();
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>Helper to fetch a string array argument.</summary>
    public static IReadOnlyList<string> StrArray(JsonObject? args, string name)
    {
        if (args is null || !args.TryGetPropertyValue(name, out JsonNode? n) || n is not JsonArray arr)
        {
            return Array.Empty<string>();
        }

        return arr.Where(x => x is not null).Select(x => x!.ToString()).ToArray();
    }
}
