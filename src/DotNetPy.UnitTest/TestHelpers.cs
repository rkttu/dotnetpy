namespace DotNetPy.UnitTest;

/// <summary>
/// Helper class for test environment detection and conditional test skipping.
/// </summary>
internal static class TestHelpers
{
    /// <summary>
    /// Returns true if running on Linux with CI environment where Python native extension modules
    /// may not work properly due to RTLD_LOCAL symbol loading issues.
    /// </summary>
    public static bool ShouldSkipNativeExtensionTests()
    {
        // Skip on Linux CI environments where Python native extensions have symbol issues
        bool isLinux = OperatingSystem.IsLinux();
        bool isCI = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ||
                    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));

        return isLinux && isCI;
    }

    /// <summary>
    /// Skips the current test if native Python extension modules are not expected to work.
    /// Call this at the beginning of tests that require modules like math, statistics, struct, base64, etc.
    /// </summary>
    public static void SkipIfNativeExtensionsUnavailable()
    {
        if (ShouldSkipNativeExtensionTests())
        {
            Assert.Inconclusive(
                "Test skipped: Python native extension modules (math, struct, base64, etc.) " +
                "are not available on Linux CI due to RTLD_LOCAL symbol loading limitations.");
        }
    }
}
