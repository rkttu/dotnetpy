namespace DotNetPy;

/// <summary>
/// Options for Python discovery.
/// </summary>
public sealed class PythonDiscoveryOptions
{
    /// <summary>
    /// Gets or sets the minimum required Python version.
    /// </summary>
    public Version? MinimumVersion { get; set; }

    /// <summary>
    /// Gets or sets the maximum allowed Python version.
    /// </summary>
    public Version? MaximumVersion { get; set; }

    /// <summary>
    /// Gets or sets the required architecture.
    /// </summary>
    public Architecture? RequiredArchitecture { get; set; }

    /// <summary>
    /// Gets or sets whether to force a refresh of cached discovery results.
    /// </summary>
    public bool ForceRefresh { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to include pre-release Python versions.
    /// </summary>
    public bool IncludePreRelease { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to search for Python installations managed by uv (global installations).
    /// </summary>
    public bool IncludeUvManagedPython { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to search for uv project local virtual environments (.venv).
    /// When enabled, the discovery will look for .venv directory in the working directory
    /// and parent directories, which is created by 'uv sync' or 'uv venv' commands.
    /// </summary>
    public bool IncludeUvProjectEnvironment { get; set; } = true;

    /// <summary>
    /// Gets or sets the working directory to start searching for uv project environments.
    /// If null, the current working directory is used.
    /// This is useful for .NET file-based apps placed inside uv project directories.
    /// </summary>
    public string? WorkingDirectory { get; set; }
}
