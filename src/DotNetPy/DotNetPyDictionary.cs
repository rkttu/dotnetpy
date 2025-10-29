namespace DotNetPy;

/// <summary>
/// A disposable dictionary containing PyValue objects.
/// </summary>
public sealed class DotNetPyDictionary : IDisposable
{
    private readonly IReadOnlyDictionary<string, DotNetPyValue?> _variables;
    private bool _disposed = false;

    internal DotNetPyDictionary(IReadOnlyDictionary<string, DotNetPyValue?> variables)
    {
        _variables = variables;
    }

    /// <summary>
    /// Gets the value of a variable.
    /// </summary>
    public DotNetPyValue? this[string key] => _variables[key];

    /// <summary>
    /// Checks if a variable exists.
    /// </summary>
    public bool ContainsKey(string key) => _variables.ContainsKey(key);

    /// <summary>
    /// Safely gets a variable.
    /// </summary>
    public bool TryGetValue(string key, out DotNetPyValue? value)
        => _variables.TryGetValue(key, out value);

    /// <summary>
    /// Gets all variable names.
    /// </summary>
    public IEnumerable<string> Keys => _variables.Keys;

    /// <summary>
    /// Gets all variable values.
    /// </summary>
    public IEnumerable<DotNetPyValue?> Values => _variables.Values;

    /// <summary>
    /// Gets the number of variables.
    /// </summary>
    public int Count => _variables.Count;

    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (var value in _variables.Values)
        {
            value?.Dispose();
        }

        _disposed = true;
    }
}
