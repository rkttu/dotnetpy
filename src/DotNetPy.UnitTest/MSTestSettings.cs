// Run all tests sequentially
// SequentialTestRunner runs all tests in a single test method.
[assembly: Parallelize(Workers = 1, Scope = ExecutionScope.MethodLevel)]
[assembly: DoNotParallelize]
