namespace DotNetPy.Uv;

/// <summary>
/// Represents a Python package dependency with optional version constraints.
/// </summary>
public sealed class PythonDependency
{
    /// <summary>
    /// Gets the package name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the version constraint (e.g., ">=1.0.0", "==2.0.0", ">=1.0,&lt;2.0").
    /// Null means any version.
    /// </summary>
    public string? VersionConstraint { get; }

    /// <summary>
    /// Gets optional extras to install (e.g., ["dev", "test"]).
    /// </summary>
    public IReadOnlyList<string> Extras { get; }

    /// <summary>
    /// Creates a new Python dependency.
    /// </summary>
    /// <param name="name">The package name.</param>
    /// <param name="versionConstraint">Optional version constraint.</param>
    /// <param name="extras">Optional extras to install.</param>
    public PythonDependency(string name, string? versionConstraint = null, IReadOnlyList<string>? extras = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Package name cannot be empty.", nameof(name));

        Name = name.Trim();
        VersionConstraint = versionConstraint?.Trim();
        Extras = extras ?? [];
    }

    /// <summary>
    /// Converts the dependency to a PEP 508 compatible string for pyproject.toml.
    /// </summary>
    public string ToPep508String()
    {
        var result = Name;

        if (Extras.Count > 0)
        {
            result += $"[{string.Join(",", Extras)}]";
        }

        if (!string.IsNullOrEmpty(VersionConstraint))
        {
            result += VersionConstraint;
        }

        return result;
    }

    /// <summary>
    /// Creates a dependency from a PEP 508 string.
    /// </summary>
    public static PythonDependency Parse(string pep508String)
    {
        if (string.IsNullOrWhiteSpace(pep508String))
            throw new ArgumentException("Dependency string cannot be empty.", nameof(pep508String));

        var input = pep508String.Trim();
        string name;
        string? version = null;
        List<string>? extras = null;

        // Parse extras [extra1,extra2]
        var extrasStart = input.IndexOf('[');
        var extrasEnd = input.IndexOf(']');
        if (extrasStart > 0 && extrasEnd > extrasStart)
        {
            var extrasStr = input.Substring(extrasStart + 1, extrasEnd - extrasStart - 1);
            extras = [.. extrasStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            input = input.Remove(extrasStart, extrasEnd - extrasStart + 1);
        }

        // Find version constraint start
        var versionStart = -1;
        foreach (var op in new[] { ">=", "<=", "==", "!=", "~=", ">", "<" })
        {
            var idx = input.IndexOf(op);
            if (idx > 0 && (versionStart < 0 || idx < versionStart))
            {
                versionStart = idx;
            }
        }

        if (versionStart > 0)
        {
            name = input[..versionStart].Trim();
            version = input[versionStart..].Trim();
        }
        else
        {
            name = input.Trim();
        }

        return new PythonDependency(name, version, extras);
    }

    /// <summary>
    /// Returns a string that represents the current object in PEP 508 format.
    /// </summary>
    /// <remarks>PEP 508 is a specification for dependency specification in Python packaging. The returned
    /// string can be used for interoperability with tools that consume PEP 508 requirement strings.</remarks>
    /// <returns>A string representation of the object formatted according to PEP 508.</returns>
    public override string ToString() => ToPep508String();
}
