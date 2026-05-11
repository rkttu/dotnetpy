namespace DotNetPy.UnitTest;

/// <summary>
/// Verifies that DotNetPy's internal temporary variables in __main__ globals are
/// uniquely named per call, leave no residue after the call, and remain isolated
/// across concurrent callers.
///
/// These properties matter for free-threaded Python (PEP 703 / 3.13t / 3.14t),
/// where the GIL no longer serializes interpreter operations and two callers
/// would otherwise race on a shared, fixed slot like _json_result.
/// </summary>
[TestClass]
public sealed class ConcurrencyAndIsolationTests
{
    private static DotNetPyExecutor _executor = default!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        try
        {
            Python.Initialize();
            _executor = Python.GetInstance();
        }
        catch (DotNetPyException ex)
        {
            Assert.Inconclusive($"Python not found: {ex.Message}");
        }
    }

    [TestInitialize]
    public void TestInitialize()
    {
        _executor.ClearGlobals();
    }

    /// <summary>
    /// Returns the list of internal _dotnetpy_* keys still present in __main__ globals.
    /// </summary>
    private static IReadOnlyList<string> GetLeftoverInternalNames()
    {
        var leftover = _executor.Evaluate("sorted([k for k in list(globals().keys()) if k.startswith('_dotnetpy_')])");
        Assert.IsNotNull(leftover);
        var result = new List<string>();
        foreach (var element in leftover.RootElement.EnumerateArray())
        {
            var s = element.GetString();
            if (s != null) result.Add(s);
        }
        return result;
    }

    [TestMethod]
    public void ExecuteAndCapture_LeavesNoInternalResidue()
    {
        using var _ = _executor.ExecuteAndCapture("result = 42");
        CollectionAssert.AreEqual(Array.Empty<string>(), GetLeftoverInternalNames().ToArray());
    }

    [TestMethod]
    public void ExecuteAndCaptureWithVariables_LeavesNoInternalResidue()
    {
        using var _ = _executor.ExecuteAndCapture(
            "result = sum(numbers)",
            new Dictionary<string, object?> { { "numbers", new[] { 1, 2, 3 } } });
        CollectionAssert.AreEqual(Array.Empty<string>(), GetLeftoverInternalNames().ToArray());
    }

    [TestMethod]
    public void CaptureVariable_LeavesNoInternalResidue()
    {
        _executor.Execute("answer = 7");
        using var _ = _executor.CaptureVariable("answer");
        CollectionAssert.AreEqual(Array.Empty<string>(), GetLeftoverInternalNames().ToArray());
    }

    [TestMethod]
    public void CaptureVariables_LeavesNoInternalResidue()
    {
        _executor.Execute("a = 1; b = 2; c = 3");
        using var _ = _executor.CaptureVariables("a", "b", "c");
        CollectionAssert.AreEqual(Array.Empty<string>(), GetLeftoverInternalNames().ToArray());
    }

    [TestMethod]
    public void VariableExists_LeavesNoInternalResidue()
    {
        _executor.Execute("present = 1");
        Assert.IsTrue(_executor.VariableExists("present"));
        Assert.IsFalse(_executor.VariableExists("absent"));
        CollectionAssert.AreEqual(Array.Empty<string>(), GetLeftoverInternalNames().ToArray());
    }

    [TestMethod]
    public void GetExistingVariables_LeavesNoInternalResidue()
    {
        _executor.Execute("x = 1; y = 2");
        var existing = _executor.GetExistingVariables("x", "y", "z");
        CollectionAssert.AreEquivalent(new[] { "x", "y" }, existing.ToArray());
        CollectionAssert.AreEqual(Array.Empty<string>(), GetLeftoverInternalNames().ToArray());
    }

    [TestMethod]
    public void DeleteVariable_LeavesNoInternalResidue()
    {
        _executor.Execute("doomed = 1");
        Assert.IsTrue(_executor.DeleteVariable("doomed"));
        CollectionAssert.AreEqual(Array.Empty<string>(), GetLeftoverInternalNames().ToArray());
    }

    [TestMethod]
    public void DeleteVariables_LeavesNoInternalResidue()
    {
        _executor.Execute("a = 1; b = 2");
        var deleted = _executor.DeleteVariables("a", "b", "missing");
        CollectionAssert.AreEquivalent(new[] { "a", "b" }, deleted.ToArray());
        CollectionAssert.AreEqual(Array.Empty<string>(), GetLeftoverInternalNames().ToArray());
    }

    [TestMethod]
    public void RepeatedCalls_DoNotAccumulateInternalNames()
    {
        // Run a mix of operations many times. Without per-call unique names this would
        // already work under GIL builds (the GIL serializes), but the goal here is to
        // assert the cleanup invariant explicitly so a regression can't sneak back in.
        for (int i = 0; i < 50; i++)
        {
            using var _ = _executor.ExecuteAndCapture($"result = {i} * 2");
            using var __ = _executor.CaptureVariable("result");
            _executor.VariableExists("result");
        }

        CollectionAssert.AreEqual(Array.Empty<string>(), GetLeftoverInternalNames().ToArray());
    }

    /// <summary>
    /// Drives many ExecuteAndCapture calls concurrently from the .NET side, each with
    /// caller-unique user variable names. The point is to exercise DotNetPy's internal
    /// scratch-name isolation — the per-call _dotnetpy_* unique naming must hold up so
    /// that no caller observes another caller's serialised result.
    ///
    /// This test deliberately avoids the orthogonal "concurrent callers reusing the
    /// same user variable name in __main__" race; that one is documented separately
    /// in <see cref="KnownLimitation_ParallelCallsWithSharedUserVariableNames_RaceUnderFT"/>.
    /// </summary>
    [TestMethod]
    public void ParallelExecuteAndCapture_EachCallerReceivesItsOwnResult()
    {
        const int callerCount = 16;
        const int iterationsPerCaller = 8;

        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

        var tasks = Enumerable.Range(0, callerCount).Select(callerId => Task.Run(() =>
        {
            // Each caller owns its own user variable namespace. DotNetPy stores user
            // variables in shared __main__ globals, so concurrent callers using the
            // same names would race; that is a separate concern from internal-name
            // isolation, which is what THIS test targets.
            string seedVar = $"seed_caller_{callerId}";
            string resultVar = $"result_caller_{callerId}";

            for (int i = 0; i < iterationsPerCaller; i++)
            {
                int seed = callerId * 1000 + i;
                int expectedOutput = seed * 2 + 1;

                using var value = _executor.ExecuteAndCapture(
                    $"{resultVar} = {seedVar} * 2 + 1",
                    new Dictionary<string, object?> { { seedVar, seed } },
                    resultVariable: resultVar);

                int? actual = value?.GetInt32();
                if (actual != expectedOutput)
                {
                    failures.Add($"caller {callerId} iter {i}: expected {expectedOutput}, got {actual?.ToString() ?? "null"}");
                }
            }
        })).ToArray();

        Task.WaitAll(tasks);

        if (!failures.IsEmpty)
        {
            Assert.Fail("Concurrent ExecuteAndCapture calls cross-talked on internal state:\n" + string.Join("\n", failures));
        }

        // No DotNetPy-internal scratch names should remain after all callers finish.
        // (Per-caller user variables like seed_caller_* remain in __main__ globals
        // until the next ClearGlobals; that is intentional and outside this filter.)
        CollectionAssert.AreEqual(Array.Empty<string>(), GetLeftoverInternalNames().ToArray());
    }

    /// <summary>
    /// Documents the shared-singleton limitation: <see cref="Python.GetInstance"/>
    /// injects user variables into the shared <c>__main__</c> globals dict, so
    /// two concurrent callers reusing the same user variable name will race
    /// regardless of how cleanly DotNetPy isolates its own internal scratch
    /// names. The recommended fix is now a first-class API:
    /// <see cref="Python.CreateIsolated"/> / <see cref="DotNetPyExecutor.CreateIsolated"/>
    /// gives each caller its own namespace. See
    /// <c>IsolatedExecutorTests.ParallelCallers_OwnIsolatedExecutorEach_NoRace</c>
    /// for the equivalent workload that succeeds under free-threaded builds.
    /// This test stays <c>[Ignore]</c>'d as a regression marker for the shared
    /// singleton's inherent behaviour, not as a planned future fix.
    /// </summary>
    [TestMethod]
    [Ignore("Shared singleton: user variables live in __main__. Use CreateIsolated() for concurrent callers.")]
    public void KnownLimitation_ParallelCallsWithSharedUserVariableNames_RaceUnderFT()
    {
        const int callerCount = 16;
        const int iterationsPerCaller = 8;

        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

        var tasks = Enumerable.Range(0, callerCount).Select(callerId => Task.Run(() =>
        {
            for (int i = 0; i < iterationsPerCaller; i++)
            {
                int expected = callerId * 1000 + i;
                using var value = _executor.ExecuteAndCapture(
                    "result = seed * 2 + 1",
                    new Dictionary<string, object?> { { "seed", expected } });

                int? actual = value?.GetInt32();
                int expectedOutput = expected * 2 + 1;
                if (actual != expectedOutput)
                {
                    failures.Add($"caller {callerId} iter {i}: expected {expectedOutput}, got {actual?.ToString() ?? "null"}");
                }
            }
        })).ToArray();

        Task.WaitAll(tasks);

        if (!failures.IsEmpty)
        {
            Assert.Fail("Concurrent ExecuteAndCapture calls cross-talked:\n" + string.Join("\n", failures));
        }
    }

    [TestMethod]
    public void ParallelCaptureVariable_EachCallerReceivesItsOwnValue()
    {
        // Pre-populate distinct variables for each caller, then capture them in parallel.
        const int callerCount = 12;
        for (int i = 0; i < callerCount; i++)
        {
            _executor.Execute($"v_{i} = {i * 7}");
        }

        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

        var tasks = Enumerable.Range(0, callerCount).Select(callerId => Task.Run(() =>
        {
            for (int i = 0; i < 5; i++)
            {
                using var captured = _executor.CaptureVariable($"v_{callerId}");
                int? actual = captured?.GetInt32();
                if (actual != callerId * 7)
                {
                    failures.Add($"caller {callerId} iter {i}: expected {callerId * 7}, got {actual?.ToString() ?? "null"}");
                }
            }
        })).ToArray();

        Task.WaitAll(tasks);

        if (!failures.IsEmpty)
        {
            Assert.Fail("Concurrent CaptureVariable calls cross-talked:\n" + string.Join("\n", failures));
        }

        CollectionAssert.AreEqual(Array.Empty<string>(), GetLeftoverInternalNames().ToArray());
    }

    [TestMethod]
    public void ParallelVariableExistsAndDelete_RemainConsistent()
    {
        // Every caller manages its own private variable to avoid logical races over
        // shared user state. The fix only affects DotNetPy's internal scratch names,
        // not user-defined globals.
        const int callerCount = 10;
        for (int i = 0; i < callerCount; i++)
        {
            _executor.Execute($"u_{i} = {i}");
        }

        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

        var tasks = Enumerable.Range(0, callerCount).Select(callerId => Task.Run(() =>
        {
            string name = $"u_{callerId}";
            if (!_executor.VariableExists(name))
                failures.Add($"caller {callerId}: variable {name} missing before delete");

            if (!_executor.DeleteVariable(name))
                failures.Add($"caller {callerId}: DeleteVariable returned false for {name}");

            if (_executor.VariableExists(name))
                failures.Add($"caller {callerId}: variable {name} still present after delete");
        })).ToArray();

        Task.WaitAll(tasks);

        if (!failures.IsEmpty)
        {
            Assert.Fail("Concurrent VariableExists/Delete calls disagreed:\n" + string.Join("\n", failures));
        }

        CollectionAssert.AreEqual(Array.Empty<string>(), GetLeftoverInternalNames().ToArray());
    }
}
