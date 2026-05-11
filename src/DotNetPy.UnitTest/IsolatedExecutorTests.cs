namespace DotNetPy.UnitTest;

/// <summary>
/// Verifies <see cref="DotNetPyExecutor.CreateIsolated"/>: each isolated
/// executor owns its own Python namespace and runs independently of the
/// shared singleton and of other isolated executors. This is the primary
/// mechanism for safe concurrent execution under free-threaded Python.
/// </summary>
[TestClass]
public sealed class IsolatedExecutorTests
{
    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        try
        {
            Python.Initialize();
            _ = Python.GetInstance();
        }
        catch (DotNetPyException ex)
        {
            Assert.Inconclusive($"Python not found: {ex.Message}");
        }
    }

    [TestMethod]
    public void CreateIsolated_ReturnsDistinctInstancePerCall()
    {
        using var a = Python.CreateIsolated();
        using var b = Python.CreateIsolated();
        Assert.AreNotSame(a, b);
        Assert.AreNotSame(a, Python.GetInstance());
    }

    [TestMethod]
    public void IsolatedExecutor_DoesNotLeakIntoSharedNamespace()
    {
        var shared = Python.GetInstance();
        shared.ClearGlobals();

        using (var iso = Python.CreateIsolated())
        {
            iso.Execute("secret = 'in-isolated'");
            Assert.IsTrue(iso.VariableExists("secret"));
        }

        Assert.IsFalse(shared.VariableExists("secret"),
            "Isolated executor's variables must not appear in the shared singleton's __main__.");
    }

    [TestMethod]
    public void IsolatedExecutors_AreIndependentFromEachOther()
    {
        using var a = Python.CreateIsolated();
        using var b = Python.CreateIsolated();

        a.Execute("x = 'alpha'");
        b.Execute("x = 'beta'");

        using var fromA = a.CaptureVariable("x");
        using var fromB = b.CaptureVariable("x");

        Assert.AreEqual("alpha", fromA?.GetString());
        Assert.AreEqual("beta", fromB?.GetString());
    }

    [TestMethod]
    public void IsolatedExecutor_PersistsVariablesAcrossCalls()
    {
        using var iso = Python.CreateIsolated();
        iso.Execute("counter = 10");
        iso.Execute("counter += 5");
        using var v = iso.CaptureVariable("counter");
        Assert.AreEqual(15, v?.GetInt32());
    }

    [TestMethod]
    public void IsolatedExecutor_HasBuiltinsAvailable()
    {
        using var iso = Python.CreateIsolated();

        // print, len, range, etc. all live in __builtins__ — the namespace must
        // have a reference to it or these would NameError.
        using var v = iso.ExecuteAndCapture("result = len([1, 2, 3, 4])");
        Assert.AreEqual(4, v?.GetInt32());

        // import works (modules go into sys.modules; this binding lands in the
        // isolated namespace).
        iso.Execute("import json");
        using var dump = iso.ExecuteAndCapture("result = json.dumps({'k': 1})");
        Assert.AreEqual("{\"k\": 1}", dump?.GetString());
    }

    [TestMethod]
    public void IsolatedExecutor_DisposeReleasesNamespace_OthersUnaffected()
    {
        using var survivor = Python.CreateIsolated();
        survivor.Execute("anchor = 1");

        var doomed = Python.CreateIsolated();
        doomed.Execute("anchor = 999");
        doomed.Dispose();

        // Survivor's state must be intact after the other executor is disposed.
        using var v = survivor.CaptureVariable("anchor");
        Assert.AreEqual(1, v?.GetInt32());
    }

    /// <summary>
    /// The same workload that races under the shared singleton (documented as
    /// <c>KnownLimitation_ParallelCallsWithSharedUserVariableNames_RaceUnderFT</c>
    /// in <see cref="ConcurrencyAndIsolationTests"/>) succeeds when each caller
    /// drives its own isolated executor.
    /// </summary>
    [TestMethod]
    public void ParallelCallers_OwnIsolatedExecutorEach_NoRace()
    {
        const int callerCount = 16;
        const int iterationsPerCaller = 8;
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

        var tasks = Enumerable.Range(0, callerCount).Select(callerId => Task.Run(() =>
        {
            using var iso = Python.CreateIsolated();
            for (int i = 0; i < iterationsPerCaller; i++)
            {
                int seed = callerId * 1000 + i;
                int expected = seed * 2 + 1;

                // Crucially, every caller writes to the SAME user variable
                // names ('seed' and 'result'). Under the shared singleton this
                // would race; under per-caller isolated executors it cannot.
                using var value = iso.ExecuteAndCapture(
                    "result = seed * 2 + 1",
                    new Dictionary<string, object?> { { "seed", seed } });
                int? actual = value?.GetInt32();
                if (actual != expected)
                    failures.Add($"caller {callerId} iter {i}: expected {expected}, got {actual?.ToString() ?? "null"}");
            }
        })).ToArray();
        Task.WaitAll(tasks);

        if (!failures.IsEmpty)
            Assert.Fail("Per-caller isolated executors raced (should be impossible):\n" + string.Join("\n", failures));
    }
}
