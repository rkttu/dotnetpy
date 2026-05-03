using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DotNetPy.Uv;

/// <summary>
/// Provides functionality to interact with the uv CLI for Python environment management.
/// </summary>
public static class UvCli
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

                    For more information: https://docs.astral.sh/uv/getting-started/installation/
                    """;
            }
            else
            {
                return """
                    uv is not installed. Install it using one of the following methods:

                    # Using curl (recommended)
                    curl -LsSf https://astral.sh/uv/install.sh | sh

                    # Or using pip
                    pip install uv

                    For more information: https://docs.astral.sh/uv/getting-started/installation/
                    """;
            }
        }
    }

    /// <summary>
    /// Ensures uv is available, throwing an exception with installation instructions if not.
    /// </summary>
    /// <exception cref="DotNetPyException">Thrown when uv is not available.</exception>
    public static void EnsureAvailable()
    {
        if (!IsAvailable)
        {
            throw new DotNetPyException(
                $"uv CLI is required but not found.\n\n{InstallationInstructions}");
        }
    }

    /// <summary>
    /// Runs a uv command asynchronously.
    /// </summary>
    /// <param name="arguments">The arguments to pass to uv.</param>
    /// <param name="workingDirectory">The working directory for the command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple containing success status, output, and error.</returns>
    public static async Task<(bool Success, string Output, string Error)> RunAsync(
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        return await RunCommandAsync("uv", arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a uv command synchronously.
    /// </summary>
    /// <param name="arguments">The arguments to pass to uv.</param>
    /// <param name="workingDirectory">The working directory for the command.</param>
    /// <returns>A tuple containing success status, output, and error.</returns>
    /// <remarks>
    /// The async work runs on a thread-pool thread to avoid sync-over-async deadlocks
    /// when called from threads that carry a SynchronizationContext (e.g. UI threads).
    /// </remarks>
    public static (bool Success, string Output, string Error) Run(
        string arguments,
        string? workingDirectory = null)
    {
        return Task.Run(() => RunAsync(arguments, workingDirectory)).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Attempts to install uv using the platform-specific installer.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if installation was successful.</returns>
    public static async Task<bool> TryInstallAsync(CancellationToken cancellationToken = default)
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

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

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

    internal static async Task<(bool Success, string Output, string Error)> RunCommandAsync(
        string command,
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);

            return (process.ExitCode == 0, output, error);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore kill errors
            }
            throw;
        }
        catch (Exception ex)
        {
            return (false, string.Empty, ex.Message);
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
            process.WaitForExit(5000);

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
