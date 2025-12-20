namespace DotNetPy.UnitTest.Integration;

/// <summary>
/// Integration tests for NumPy operations using uv-managed Python environment.
/// </summary>
[TestClass]
public sealed class NumPyIntegrationTests
{
    private static UvEnvironmentFixture? _fixture;
    private static bool _numpyInstalled;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        if (!UvCliHelper.IsAvailable)
        {
            context.WriteLine(UvCliHelper.GetSkipMessage());
            return;
        }

        _fixture = new UvEnvironmentFixture();
        var initialized = await _fixture.InitializeAsync();
        
        if (initialized)
        {
            _numpyInstalled = await _fixture.InstallPackagesAsync("numpy");
            if (_numpyInstalled)
            {
                context.WriteLine("NumPy installed successfully.");
            }
        }
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _fixture?.Dispose();
    }

    private void EnsureNumPyReady()
    {
        if (!UvCliHelper.IsAvailable)
        {
            Assert.Inconclusive(UvCliHelper.GetSkipMessage());
        }
        if (_fixture == null || !_fixture.IsEnvironmentReady)
        {
            Assert.Inconclusive("UV environment is not available.");
        }
        if (!_numpyInstalled)
        {
            Assert.Inconclusive("NumPy installation failed.");
        }
    }

    [TestMethod]
    public async Task NumPy_ArrayOperations()
    {
        EnsureNumPyReady();

        var (success, output, error) = await _fixture!.RunPythonScriptAsync(@"
import numpy as np

arr = np.array([1, 2, 3, 4, 5])
print(f'Sum: {np.sum(arr)}')
print(f'Mean: {np.mean(arr)}')
print(f'Std: {np.std(arr):.4f}')
");

        Assert.IsTrue(success, $"NumPy script failed: {error}");
        Assert.Contains("Sum: 15", output);
        Assert.Contains("Mean: 3.0", output);
    }

    [TestMethod]
    public async Task NumPy_MatrixOperations()
    {
        EnsureNumPyReady();

        var (success, output, error) = await _fixture!.RunPythonScriptAsync(@"
import numpy as np

a = np.array([[1, 2], [3, 4]])
b = np.array([[5, 6], [7, 8]])
c = np.dot(a, b)

print(f'Result[0,0]: {c[0,0]}')
print(f'Result[1,1]: {c[1,1]}')
");

        Assert.IsTrue(success, $"NumPy matrix script failed: {error}");
        Assert.Contains("Result[0,0]: 19", output);  // 1*5 + 2*7 = 19
        Assert.Contains("Result[1,1]: 50", output);  // 3*6 + 4*8 = 50
    }

    [TestMethod]
    public async Task NumPy_LinearAlgebra()
    {
        EnsureNumPyReady();

        var (success, output, error) = await _fixture!.RunPythonScriptAsync(@"
import numpy as np

# Create a 2x2 matrix
matrix = np.array([[4, 7], [2, 6]])

# Calculate determinant
det = np.linalg.det(matrix)
print(f'Determinant: {det:.1f}')

# Calculate inverse
inv = np.linalg.inv(matrix)
print(f'Inverse[0,0]: {inv[0,0]:.1f}')
");

        Assert.IsTrue(success, $"NumPy linalg script failed: {error}");
        Assert.Contains("Determinant: 10.0", output);  // 4*6 - 7*2 = 10
    }
}
