using System.Text.Json;

namespace DotNetPy;

/// <summary>
/// A wrapper class for values returned from Python.
/// Manages the disposal of JsonDocument and provides methods for safe access to values.
/// </summary>
public sealed class DotNetPyValue : IDisposable
{
    private readonly JsonDocument _doc;
    private bool _disposed;

    internal DotNetPyValue(JsonDocument doc)
    {
        _doc = doc;
    }

    public string? GetString(string path = "")
    {
        if (TryGetProperty(path, out var element))
        {
            return element.GetString();
        }
        return null;
    }

    public int? GetInt32(string path = "")
    {
        if (TryGetProperty(path, out var element) &&
            element.TryGetInt32(out var value))
        {
            return value;
        }
        return null;
    }

    public double? GetDouble(string path = "")
    {
        if (TryGetProperty(path, out var element) &&
            element.TryGetDouble(out var value))
        {
            return value;
        }
        return null;
    }

    public bool? GetBoolean(string path = "")
    {
        if (TryGetProperty(path, out var element))
        {
            if (element.ValueKind == JsonValueKind.True) return true;
            if (element.ValueKind == JsonValueKind.False) return false;
        }
        return null;
    }

    public JsonElement? GetProperty(string path)
    {
        if (TryGetProperty(path, out var element))
        {
            return element;
        }
        return null;
    }

    public Dictionary<string, object?>? ToDictionary()
    {
        if (_doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        return ToObject(_doc.RootElement) as Dictionary<string, object?>;
    }

    public IReadOnlyList<object?>? ToList()
    {
        if (_doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return ToObject(_doc.RootElement) as IReadOnlyList<object?>;
    }

    private static object? ToObject(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var dictionary = new Dictionary<string, object?>();
                foreach (var property in element.EnumerateObject())
                {
                    dictionary[property.Name] = ToObject(property.Value);
                }
                return dictionary;

            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(ToObject(item));
                }
                return list;

            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Number:
                if (element.TryGetInt64(out long l)) return l;
                return element.GetDouble();

            case JsonValueKind.True:
                return true;

            case JsonValueKind.False:
                return false;

            case JsonValueKind.Null:
                return null;

            default:
                return null;
        }
    }

    private bool TryGetProperty(string path, out JsonElement result)
    {
        if (string.IsNullOrEmpty(path))
        {
            result = _doc.RootElement;
            return true;
        }

        var parts = path.Split('.');
        var current = _doc.RootElement;

        foreach (var part in parts)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(part, out current))
            {
                result = default;
                return false;
            }
        }

        result = current;
        return true;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _doc.Dispose();
            _disposed = true;
        }
    }
}
