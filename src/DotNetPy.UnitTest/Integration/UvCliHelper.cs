using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DotNetPy.UnitTest.Integration;

/// <summary>
/// Helper class for managing uv CLI installation and availability.
/// </summary>
public static class UvCliHelper
{
    private static bool? _isAvailable;
    private static string? _version;

    /// <summary>
    /// Gets whether uv CLI is available on the system.
    /// </summary>
    public static bool IsAvailable
    {
        get
        {
            _isAvailable ??= CheckAvailability();
            return _isAvailable.Value;
        }
    }

    /// <summary>
    /// Gets the uv version string if available.
    /// </summary>
    public static string? Version
    {
        get
        {
            if (_version == null && IsAvailable)
            {
                _version = GetVersion();
            }
            return _version;
        }
    }

    /// <summary>
    /// Gets the installation instructions for the current platform.
    /// </summary>
    public static string InstallationInstructions
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return """
                    uv is not installed. Install it using one of the following methods:

                    # PowerShell (recommended)
                    powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"

                    # Or using pip
                    pip install uv

                    # Or using pipx
                    pipx install uv

                    # Or using Scoop
                    scoop install uv

                    # Or using WinGet
                    winget install astral-sh.uv

                    For more information: https://docs.astral.sh/uv/getting-started/installation/
                    """;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return """
                    uv is not installed. Install it using one of the following methods:

                    # Using curl (recommended)
                    curl -LsSf https://astral.sh/uv/install.sh | sh

                    # Or using Homebrew
                    brew install uv

                    # Or using pip
                    pip install uv

                    For more information: https://docs.astral.sh/uv/getting-started/installation/
                    """;
            }
            else // Linux
            {
                return """
                    uv is not installed. Install it using one of the following methods:

                    # Using curl (recommended)
                    curl -LsSf https://astral.sh/uv/install.sh | sh

                    # Or using pip
                    pip install uv

                    # Or using pipx
                    pipx install uv

                    For more information: https://docs.astral.sh/uv/getting-started/installation/
                    """;
            }
        }
    }

    /// <summary>
    /// Checks if uv is available and throws an informative exception if not.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when uv is not available.</exception>
    public static void EnsureAvailable()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(
                $"uv CLI is required for integration tests.\n\n{InstallationInstructions}");
        }
    }

    /// <summary>
    /// Gets a skip message for tests when uv is not available.
    /// </summary>
    public static string GetSkipMessage()
    {
        return $"uv CLI is not available. {InstallationInstructions}";
    }

    /// <summary>
    /// Attempts to install uv using the platform-specific installer.
    /// </summary>
    /// <returns>True if installation was successful.</returns>
    public static async Task<bool> TryInstallAsync()
    {
        try
        {
            ProcessStartInfo psi;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-ExecutionPolicy ByPass -c \"irm https://astral.sh/uv/install.ps1 | iex\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
            }
            else
            {
                psi = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = "-c \"curl -LsSf https://astral.sh/uv/install.sh | sh\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
            }

            using var process = Process.Start(psi);
            if (process == null)
                return false;

            await process.WaitForExitAsync();

            // Reset cached availability
            _isAvailable = null;
            _version = null;

            return process.ExitCode == 0 && IsAvailable;
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckAvailability()
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "uv",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            process.Start();
            process.WaitForExit(5000); // 5 second timeout

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetVersion()
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "uv",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            return output.Trim();
        }
        catch
        {
            return null;
        }
    }
}
