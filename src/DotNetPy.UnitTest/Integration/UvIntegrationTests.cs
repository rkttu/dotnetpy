namespace DotNetPy.UnitTest.Integration;

/// <summary>
/// Integration tests using Python uv-based virtual environments.
/// These tests verify DotNetPy works correctly with isolated Python environments
/// and third-party packages.
/// </summary>
[TestClass]
public sealed class UvIntegrationTests
{
    private static UvEnvironmentFixture? _fixture;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        // Check uv availability first
        if (!UvCliHelper.IsAvailable)
        {
            context.WriteLine(UvCliHelper.GetSkipMessage());
            return;
        }

        context.WriteLine($"uv CLI detected: {UvCliHelper.Version}");

        _fixture = new UvEnvironmentFixture();
        var initialized = await _fixture.InitializeAsync(pythonVersion: null, packages: []);
        
        if (!initialized)
        {
            context.WriteLine("UV environment initialization failed. Tests will be skipped.");
        }
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _fixture?.Dispose();
    }

    private void EnsureEnvironmentReady()
    {
        if (!UvCliHelper.IsAvailable)
        {
            Assert.Inconclusive(UvCliHelper.GetSkipMessage());
        }

        if (_fixture == null || !_fixture.IsEnvironmentReady)
        {
            Assert.Inconclusive("UV environment is not available.");
        }
    }

    [TestMethod]
    public void UvCli_IsInstalled()
    {
        if (!UvCliHelper.IsAvailable)
        {
            Assert.Inconclusive(UvCliHelper.GetSkipMessage());
        }

        Assert.IsTrue(UvCliHelper.IsAvailable);
        Assert.IsNotNull(UvCliHelper.Version);
        Console.WriteLine($"uv version: {UvCliHelper.Version}");
    }

    [TestMethod]
    public void UvEnvironment_PythonVersionDetected()
    {
        EnsureEnvironmentReady();

        Assert.IsNotNull(_fixture!.PythonVersion);
        Console.WriteLine($"Python version: {_fixture.PythonVersion}");
    }

    [TestMethod]
    public void UvEnvironment_PythonExecutableExists()
    {
        EnsureEnvironmentReady();

        Assert.IsTrue(File.Exists(_fixture!.PythonExecutable));
        Console.WriteLine($"Python executable: {_fixture.PythonExecutable}");
    }

    [TestMethod]
    public async Task UvEnvironment_CanRunPythonScript()
    {
        EnsureEnvironmentReady();

        var (success, output, error) = await _fixture!.RunPythonScriptAsync("print('Hello from uv!')");

        Assert.IsTrue(success, $"Script failed: {error}");
        Assert.Contains("Hello from uv!", output);
    }

    [TestMethod]
    public async Task UvEnvironment_CanInstallAndUsePackage()
    {
        EnsureEnvironmentReady();

        // Install a simple package
        var installed = await _fixture!.InstallPackagesAsync("python-dateutil");
        Assert.IsTrue(installed, "Failed to install python-dateutil");

        // Use the package
        var (success, output, error) = await _fixture.RunPythonScriptAsync(@"
from dateutil import parser
dt = parser.parse('2024-01-15')
print(f'Parsed: {dt.year}-{dt.month}-{dt.day}')
");

        Assert.IsTrue(success, $"Script failed: {error}");
        Assert.Contains("Parsed: 2024-1-15", output);
    }
}
