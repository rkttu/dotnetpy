# Performance and Concurrency

## Thread Safety

DotNetPy is **thread-safe** through Python's Global Interpreter Lock (GIL). Multiple threads can safely call executor methods concurrently without additional synchronization. However, there are important performance considerations:

- Python execution is inherently **serialized** - only one thread executes Python code at a time due to the GIL
- Multiple concurrent threads will compete for the GIL, which can lead to performance degradation under high contention

## Performance Considerations

**DotNetPy is not designed for high-concurrency scenarios** involving many threads simultaneously executing Python code. The library is best suited for:

✅ **Recommended Use Cases:**

- Sequential Python script execution
- I/O-bound operations where threads naturally yield
- Low to moderate concurrency (2-5 concurrent operations)
- Scripting and automation tasks
- Data processing workflows with reasonable parallelism

❌ **Not Recommended:**

- High-frequency Python calls from 10+ concurrent threads
- CPU-intensive parallel processing relying on Python
- Real-time systems requiring predictable low-latency responses
- Scenarios where Python becomes a bottleneck in a high-throughput pipeline

## Design Philosophy

DotNetPy provides a **safe and convenient** bridge between .NET and Python, respecting Python's inherent characteristics rather than attempting to work around them. The library exposes Python's native behavior transparently:

- **GIL Contention**: Under extreme concurrency (e.g., 20+ threads), you may experience significant performance degradation or timeouts. This is a fundamental Python limitation, not a library bug.
- **No Magic Solutions**: We do not add complex synchronization layers that would hide Python's true performance characteristics or add unpredictable overhead.

## Alternative Approaches

For CPU-intensive parallel workloads, consider:

- **Pure .NET solutions** for performance-critical parallel processing
- **Python multiprocessing** (separate processes) for true parallelism in Python
- **Task-based patterns** that minimize concurrent Python calls
