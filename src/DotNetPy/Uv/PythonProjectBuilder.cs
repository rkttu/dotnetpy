using System.Text;

namespace DotNetPy.Uv;

/// <summary>
/// Builder class for creating a Python project configuration declaratively.
/// Generates pyproject.toml and manages uv-based Python environments.
/// </summary>
public sealed class PythonProjectBuilder
{
    private string _projectName = "dotnetpy-project";
    private string _version = "1.0.0";
    private string? _description;
    private string? _pythonVersion;
    private readonly List<PythonDependency> _dependencies = [];
    private readonly List<PythonDependency> _devDependencies = [];
    private readonly Dictionary<string, string> _uvSettings = [];
    private string? _workingDirectory;

    /// <summary>
    /// Creates a new Python project builder.
    /// </summary>
    public PythonProjectBuilder()
    {
    }

    /// <summary>
    /// Sets the project name.
    /// </summary>
    /// <param name="name">The project name.</param>
    /// <returns>The builder instance for chaining.</returns>
    public PythonProjectBuilder WithProjectName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Project name cannot be empty.", nameof(name));

        _projectName = name.Trim();
        return this;
    }

    /// <summary>
    /// Sets the project version.
    /// </summary>
    /// <param name="version">The version string.</param>
    /// <returns>The builder instance for chaining.</returns>
    public PythonProjectBuilder WithVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version cannot be empty.", nameof(version));

        _version = version.Trim();
        return this;
    }

    /// <summary>
    /// Sets the project description.
    /// </summary>
    /// <param name="description">The description.</param>
    /// <returns>The builder instance for chaining.</returns>
    public PythonProjectBuilder WithDescription(string description)
    {
        _description = description?.Trim();
        return this;
    }

    /// <summary>
    /// Sets the required Python version constraint.
    /// </summary>
    /// <param name="versionConstraint">Python version constraint (e.g., ">=3.10", ">=3.10,&lt;4.0", "3.11").</param>
    /// <returns>The builder instance for chaining.</returns>
    public PythonProjectBuilder WithPythonVersion(string versionConstraint)
    {
        if (string.IsNullOrWhiteSpace(versionConstraint))
            throw new ArgumentException("Python version constraint cannot be empty.", nameof(versionConstraint));

        // Normalize: if just a version number, prefix with >=
        var normalized = versionConstraint.Trim();
        if (char.IsDigit(normalized[0]))
        {
            normalized = $">={normalized}";
        }

        _pythonVersion = normalized;
        return this;
    }

    /// <summary>
    /// Adds a package dependency.
    /// </summary>
    /// <param name="packageName">The package name.</param>
    /// <param name="versionConstraint">Optional version constraint (e.g., ">=1.0.0").</param>
    /// <returns>The builder instance for chaining.</returns>
    public PythonProjectBuilder AddDependency(string packageName, string? versionConstraint = null)
    {
        _dependencies.Add(new PythonDependency(packageName, versionConstraint));
        return this;
    }

    /// <summary>
    /// Adds a package dependency with extras.
    /// </summary>
    /// <param name="packageName">The package name.</param>
    /// <param name="versionConstraint">Optional version constraint.</param>
    /// <param name="extras">Extras to install.</param>
    /// <returns>The builder instance for chaining.</returns>
    public PythonProjectBuilder AddDependency(string packageName, string? versionConstraint, params string[] extras)
    {
        _dependencies.Add(new PythonDependency(packageName, versionConstraint, extras));
        return this;
    }

    /// <summary>
    /// Adds a dependency from a PEP 508 string.
    /// </summary>
    /// <param name="pep508String">The PEP 508 dependency string (e.g., "numpy>=1.24.0").</param>
    /// <returns>The builder instance for chaining.</returns>
    public PythonProjectBuilder AddDependency(string pep508String)
    {
        _dependencies.Add(PythonDependency.Parse(pep508String));
        return this;
    }

    /// <summary>
    /// Adds multiple dependencies.
    /// </summary>
    /// <param name="dependencies">The dependencies to add.</param>
    /// <returns>The builder instance for chaining.</returns>
    public PythonProjectBuilder AddDependencies(params PythonDependency[] dependencies)
    {
        _dependencies.AddRange(dependencies);
        return this;
    }

    /// <summary>
    /// Adds multiple dependencies from PEP 508 strings.
    /// </summary>
    /// <param name="pep508Strings">The PEP 508 dependency strings.</param>
    /// <returns>The builder instance for chaining.</returns>
    public PythonProjectBuilder AddDependencies(params string[] pep508Strings)
    {
        foreach (var s in pep508Strings)
        {
            _dependencies.Add(PythonDependency.Parse(s));
        }
        return this;
    }

    /// <summary>
    /// Adds a development dependency.
    /// </summary>
    /// <param name="packageName">The package name.</param>
    /// <param name="versionConstraint">Optional version constraint.</param>
    /// <returns>The builder instance for chaining.</returns>
    public PythonProjectBuilder AddDevDependency(string packageName, string? versionConstraint = null)
    {
        _devDependencies.Add(new PythonDependency(packageName, versionConstraint));
        return this;
    }

    /// <summary>
    /// Sets the working directory for the project.
    /// If not set, a temporary directory will be used.
    /// </summary>
    /// <param name="directory">The working directory path.</param>
    /// <returns>The builder instance for chaining.</returns>
    public PythonProjectBuilder WithWorkingDirectory(string directory)
    {
        _workingDirectory = directory;
        return this;
    }

    /// <summary>
    /// Adds a uv-specific setting.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <param name="value">The setting value.</param>
    /// <returns>The builder instance for chaining.</returns>
    public PythonProjectBuilder WithUvSetting(string key, string value)
    {
        _uvSettings[key] = value;
        return this;
    }

    /// <summary>
    /// Builds and returns the Python project configuration.
    /// </summary>
    /// <returns>A new PythonProject instance.</returns>
    public PythonProject Build()
    {
        return new PythonProject(
            _projectName,
            _version,
            _description,
            _pythonVersion,
            _dependencies.ToList(),
            _devDependencies.ToList(),
            _uvSettings.ToDictionary(),
            _workingDirectory);
    }

    /// <summary>
    /// Generates the pyproject.toml content.
    /// </summary>
    /// <returns>The TOML content as a string.</returns>
    public string GeneratePyProjectToml()
    {
        var sb = new StringBuilder();

        // [project] section
        sb.AppendLine("[project]");
        sb.AppendLine($"name = \"{EscapeTomlString(_projectName)}\"");
        sb.AppendLine($"version = \"{EscapeTomlString(_version)}\"");

        if (!string.IsNullOrEmpty(_description))
        {
            sb.AppendLine($"description = \"{EscapeTomlString(_description)}\"");
        }

        if (!string.IsNullOrEmpty(_pythonVersion))
        {
            sb.AppendLine($"requires-python = \"{_pythonVersion}\"");
        }

        // Dependencies
        if (_dependencies.Count > 0)
        {
            sb.AppendLine("dependencies = [");
            foreach (var dep in _dependencies)
            {
                sb.AppendLine($"    \"{dep.ToPep508String()}\",");
            }
            sb.AppendLine("]");
        }

        // Dev dependencies (optional-dependencies.dev)
        if (_devDependencies.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[project.optional-dependencies]");
            sb.AppendLine("dev = [");
            foreach (var dep in _devDependencies)
            {
                sb.AppendLine($"    \"{dep.ToPep508String()}\",");
            }
            sb.AppendLine("]");
        }

        // [tool.uv] section
        sb.AppendLine();
        sb.AppendLine("[tool.uv]");
        sb.AppendLine("managed = true");

        foreach (var (key, value) in _uvSettings)
        {
            // Handle boolean values
            if (bool.TryParse(value, out var boolVal))
            {
                sb.AppendLine($"{key} = {boolVal.ToString().ToLowerInvariant()}");
            }
            else
            {
                sb.AppendLine($"{key} = \"{EscapeTomlString(value)}\"");
            }
        }

        return sb.ToString();
    }

    private static string EscapeTomlString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
