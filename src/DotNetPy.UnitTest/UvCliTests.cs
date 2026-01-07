using DotNetPy.Uv;

namespace DotNetPy.UnitTest;

[TestClass]
public sealed class UvCliTests
{
    [TestMethod]
    public void IsAvailable_ReturnsBoolean()
    {
        // Just verify it doesn't throw
        var result = UvCli.IsAvailable;
        Console.WriteLine($"uv is available: {result}");
    }

    [TestMethod]
    public void Version_ReturnsVersionOrNull()
    {
        if (!UvCli.IsAvailable)
        {
            Assert.Inconclusive("uv is not installed");
            return;
        }

        var version = UvCli.Version;
        Assert.IsNotNull(version);
        Console.WriteLine($"uv version: {version}");
    }

    [TestMethod]
    public void InstallationInstructions_ReturnsNonEmptyString()
    {
        var instructions = UvCli.InstallationInstructions;

        Assert.IsNotNull(instructions);
        Assert.IsGreaterThan(0, instructions.Length);
        Assert.Contains("uv", instructions);
        Console.WriteLine(instructions);
    }

    [TestMethod]
    public void EnsureAvailable_WhenNotAvailable_ThrowsDotNetPyException()
    {
        if (UvCli.IsAvailable)
        {
            // If available, should not throw
            UvCli.EnsureAvailable();
            return;
        }

        Assert.ThrowsExactly<DotNetPyException>(() => UvCli.EnsureAvailable());
    }

    [TestMethod]
    public async Task RunAsync_UvVersion_ReturnsSuccess()
    {
        if (!UvCli.IsAvailable)
        {
            Assert.Inconclusive("uv is not installed");
            return;
        }

        var (Success, Output, _) = await UvCli.RunAsync("--version");

        Assert.IsTrue(Success);
        Assert.Contains("uv", Output);
        Console.WriteLine($"Output: {Output}");
    }

    [TestMethod]
    public async Task RunAsync_InvalidCommand_ReturnsFailure()
    {
        if (!UvCli.IsAvailable)
        {
            Assert.Inconclusive("uv is not installed");
            return;
        }

        var (Success, _, _) = await UvCli.RunAsync("invalid-command-that-does-not-exist");

        Assert.IsFalse(Success);
    }

    [TestMethod]
    public void Run_Synchronous_Works()
    {
        if (!UvCli.IsAvailable)
        {
            Assert.Inconclusive("uv is not installed");
            return;
        }

        var (Success, _, _) = UvCli.Run("--version");

        Assert.IsTrue(Success);
    }

    [TestMethod]
    public async Task RunAsync_WithCancellation_ThrowsOperationCanceled()
    {
        if (!UvCli.IsAvailable)
        {
            Assert.Inconclusive("uv is not installed");
            return;
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
        {
            await UvCli.RunAsync("--help", cancellationToken: cts.Token);
        });
    }
}
