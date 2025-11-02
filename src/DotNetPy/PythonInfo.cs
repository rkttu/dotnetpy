namespace DotNetPy;

/// <summary>
/// Represents information about a discovered Python installation.
/// </summary>
public sealed class PythonInfo
{
    /// <summary>
    /// Gets the path to the Python executable (python.exe or python3).
    /// </summary>
    public required string ExecutablePath { get; init; }

    /// <summary>
    /// Gets the path to the Python shared library (e.g., python313.dll, libpython3.13.so).
    /// </summary>
    public required string LibraryPath { get; init; }

    /// <summary>
    /// Gets the Python version.
    /// </summary>
    public required Version Version { get; init; }

    /// <summary>
    /// Gets the architecture (X64, X86, Arm64, etc.).
    /// </summary>
    public required Architecture Architecture { get; init; }

    /// <summary>
    /// Gets the source where this Python was discovered from.
    /// </summary>
    public required PythonSource Source { get; init; }

    /// <summary>
    /// Gets the priority score for this Python installation (higher is better).
    /// </summary>
    internal int Priority { get; set; }

    /// <summary>
    /// Gets the home directory of the Python installation.
    /// </summary>
    public string? HomeDirectory { get; init; }

    /// <summary>
    /// Returns a string representation of the Python installation information.
    /// </summary>
    /// <returns>A formatted string containing version, architecture, source, and path information.</returns>
    public override string ToString()
        => $"Python {Version} ({Architecture}) from {Source} at {ExecutablePath}";
}

/// <summary>
/// Indicates where a Python installation was discovered from.
/// </summary>
public enum PythonSource
{
    /// <summary>
    /// Found via PATH environment variable (highest priority).
    /// </summary>
    Path = 100,

    /// <summary>
    /// Found via Python Launcher (py.exe on Windows).
    /// </summary>
    PyLauncher = 80,

    /// <summary>
    /// Found via Windows Registry.
    /// </summary>
    Registry = 60,

    /// <summary>
    /// Found via standard installation paths.
    /// </summary>
    StandardPath = 40,

    /// <summary>
    /// Provided by user explicitly.
    /// </summary>
    UserProvided = 20
}

/// <summary>
/// Represents the processor architecture.
/// </summary>
public enum Architecture
{
    /// <summary>
    /// Unknown architecture.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// x86 (32-bit Intel/AMD).
    /// </summary>
    X86 = 32,

    /// <summary>
    /// x64 (64-bit Intel/AMD).
    /// </summary>
    X64 = 64,

    /// <summary>
    /// ARM (32-bit).
    /// </summary>
    Arm = 33,

    /// <summary>
    /// ARM64 (64-bit).
    /// </summary>
    Arm64 = 65
}
