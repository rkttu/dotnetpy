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

    /// <summary>
    /// Gets the string value at the specified path.
    /// </summary>
    /// <param name="path">The property path (dot-separated). Empty string returns the root value.</param>
    /// <returns>The string value, or null if not found or not a string.</returns>
    public string? GetString(string path = "")
    {
        if (TryGetProperty(path, out var element))
        {
            return element.GetString();
        }
        return null;
    }

    /// <summary>
    /// Gets the 32-bit integer value at the specified path.
    /// </summary>
    /// <param name="path">The property path (dot-separated). Empty string returns the root value.</param>
    /// <returns>The integer value, or null if not found or not an integer.</returns>
    public int? GetInt32(string path = "")
    {
        if (TryGetProperty(path, out var element) &&
            element.TryGetInt32(out var value))
        {
            return value;
        }
        return null;
    }

    /// <summary>
    /// Gets the double-precision floating-point value at the specified path.
    /// </summary>
    /// <param name="path">The property path (dot-separated). Empty string returns the root value.</param>
    /// <returns>The double value, or null if not found or not a number.</returns>
    public double? GetDouble(string path = "")
    {
        if (TryGetProperty(path, out var element) &&
            element.TryGetDouble(out var value))
        {
            return value;
        }
        return null;
    }

    /// <summary>
    /// Gets the boolean value at the specified path.
    /// </summary>
    /// <param name="path">The property path (dot-separated). Empty string returns the root value.</param>
    /// <returns>The boolean value, or null if not found or not a boolean.</returns>
    public bool? GetBoolean(string path = "")
    {
        if (TryGetProperty(path, out var element))
        {
            if (element.ValueKind == JsonValueKind.True) return true;
            if (element.ValueKind == JsonValueKind.False) return false;
        }
        return null;
    }

    /// <summary>
    /// Gets the JSON element at the specified path.
    /// </summary>
    /// <param name="path">The property path (dot-separated).</param>
    /// <returns>The JSON element, or null if not found.</returns>
    public JsonElement? GetProperty(string path)
    {
        if (TryGetProperty(path, out var element))
        {
            return element;
        }
        return null;
    }

    /// <summary>
    /// Converts the Python dictionary to a .NET Dictionary.
    /// </summary>
    /// <returns>A Dictionary representation, or null if the value is not an object.</returns>
    public Dictionary<string, object?>? ToDictionary()
    {
        if (_doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        return ToObject(_doc.RootElement) as Dictionary<string, object?>;
    }

    /// <summary>
    /// Converts the Python list to a .NET list.
    /// </summary>
    /// <returns>A list representation, or null if the value is not an array.</returns>
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

    /// <summary>
    /// Releases the resources used by this instance.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _doc.Dispose();
            _disposed = true;
        }
    }
}
