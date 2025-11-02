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
}
