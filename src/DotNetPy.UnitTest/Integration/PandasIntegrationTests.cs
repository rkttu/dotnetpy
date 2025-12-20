namespace DotNetPy.UnitTest.Integration;

/// <summary>
/// Integration tests for Pandas operations using uv-managed Python environment.
/// </summary>
[TestClass]
public sealed class PandasIntegrationTests
{
    private static UvEnvironmentFixture? _fixture;
    private static bool _pandasInstalled;

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
            _pandasInstalled = await _fixture.InstallPackagesAsync("pandas");
            if (_pandasInstalled)
            {
                context.WriteLine("Pandas installed successfully.");
            }
        }
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _fixture?.Dispose();
    }

    private void EnsurePandasReady()
    {
        if (!UvCliHelper.IsAvailable)
        {
            Assert.Inconclusive(UvCliHelper.GetSkipMessage());
        }
        if (_fixture == null || !_fixture.IsEnvironmentReady)
        {
            Assert.Inconclusive("UV environment is not available.");
        }
        if (!_pandasInstalled)
        {
            Assert.Inconclusive("Pandas installation failed.");
        }
    }

    [TestMethod]
    public async Task Pandas_CreateDataFrame()
    {
        EnsurePandasReady();

        var (success, output, error) = await _fixture!.RunPythonScriptAsync(@"
import pandas as pd

df = pd.DataFrame({
    'name': ['Alice', 'Bob', 'Charlie'],
    'age': [25, 30, 35],
    'city': ['NYC', 'LA', 'Chicago']
})

print(f'Rows: {len(df)}')
print(f'Columns: {list(df.columns)}')
");

        Assert.IsTrue(success, $"Pandas script failed: {error}");
        Assert.Contains("Rows: 3", output);
        Assert.Contains("name", output);
        Assert.Contains("age", output);
    }

    [TestMethod]
    public async Task Pandas_DataFrameStatistics()
    {
        EnsurePandasReady();

        var (success, output, error) = await _fixture!.RunPythonScriptAsync(@"
import pandas as pd

df = pd.DataFrame({
    'values': [10, 20, 30, 40, 50]
})

print(f'Sum: {df[""values""].sum()}')
print(f'Mean: {df[""values""].mean()}')
print(f'Max: {df[""values""].max()}')
print(f'Min: {df[""values""].min()}')
");

        Assert.IsTrue(success, $"Pandas statistics script failed: {error}");
        Assert.Contains("Sum: 150", output);
        Assert.Contains("Mean: 30.0", output);
        Assert.Contains("Max: 50", output);
        Assert.Contains("Min: 10", output);
    }

    [TestMethod]
    public async Task Pandas_GroupBy()
    {
        EnsurePandasReady();

        var (success, output, error) = await _fixture!.RunPythonScriptAsync(@"
import pandas as pd

df = pd.DataFrame({
    'category': ['A', 'B', 'A', 'B', 'A'],
    'value': [10, 20, 30, 40, 50]
})

grouped = df.groupby('category')['value'].sum()
print(f'A total: {grouped[""A""]}')
print(f'B total: {grouped[""B""]}')
");

        Assert.IsTrue(success, $"Pandas groupby script failed: {error}");
        Assert.Contains("A total: 90", output);  // 10 + 30 + 50
        Assert.Contains("B total: 60", output);  // 20 + 40
    }

    [TestMethod]
    public async Task Pandas_FilterAndSort()
    {
        EnsurePandasReady();

        var (success, output, error) = await _fixture!.RunPythonScriptAsync(@"
import pandas as pd

df = pd.DataFrame({
    'name': ['Alice', 'Bob', 'Charlie', 'David'],
    'score': [85, 92, 78, 95]
})

# Filter scores > 80 and sort descending
filtered = df[df['score'] > 80].sort_values('score', ascending=False)
print(f'Top scorer: {filtered.iloc[0][""name""]}')
print(f'Count above 80: {len(filtered)}')
");

        Assert.IsTrue(success, $"Pandas filter script failed: {error}");
        Assert.Contains("Top scorer: David", output);
        Assert.Contains("Count above 80: 3", output);
    }
}
