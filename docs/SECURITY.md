# Security Considerations

DotNetPy executes arbitrary Python code with the **same privileges as the host .NET process**. This powerful capability requires careful consideration of security implications.

## Code Injection Risk

**Never pass untrusted or user-provided input directly to execution methods.** The following pattern is dangerous:

```csharp
// ❌ DANGEROUS: User input executed as code
string userInput = GetUserInput();
executor.Execute(userInput); // Remote Code Execution vulnerability!

// ❌ DANGEROUS: Unsanitized data from external sources
string scriptFromDb = database.GetScript();
executor.Execute(scriptFromDb); // Potential code injection!
```

## Safe Usage Patterns

```csharp
// ✅ SAFE: Hardcoded, developer-controlled code
executor.Execute(@"
    import statistics
    result = statistics.mean(data)
");

// ✅ SAFE: User data passed as variables, not code
var userNumbers = GetUserNumbers(); // Data, not code
executor.Execute(@"
    result = sum(numbers) / len(numbers)
", new Dictionary<string, object?> { { "numbers", userNumbers } });
```

## Python's Unrestricted Capabilities

Python code executed through DotNetPy has full access to:

- **File system**: Read, write, delete files
- **Network**: Make HTTP requests, open sockets
- **System commands**: Execute shell commands via `os.system()`, `subprocess`
- **Environment**: Access environment variables, system information
- **Process control**: Spawn processes, signal handling

## Recommendations

| Scenario | Recommendation |
| ---------- | ---------------- |
| Developer-controlled scripts | ✅ Safe to use as-is |
| Configuration-based scripts | ⚠️ Ensure configs are from trusted sources only |
| User-provided code | ❌ Not recommended without sandboxing |
| Plugin/extension systems | ❌ Requires additional isolation (containers, separate processes) |

**This library does not provide sandboxing.** If you need to execute untrusted Python code, consider:

- Running Python in a containerized environment (Docker)
- Using OS-level sandboxing (seccomp, AppArmor)
- Implementing a separate, isolated worker process

## Built-in Code Injection Analyzer

DotNetPy includes a Roslyn analyzer that automatically detects potential code injection vulnerabilities at compile time. When you pass a non-constant string to execution methods, you'll see a warning:

```csharp
string userInput = Console.ReadLine();
executor.Execute(userInput);  // ⚠️ DOTNETPY001: Potential Python code injection

executor.Execute("print('hello')");  // ✅ No warning - string literal is safe
```

The analyzer warns when:

- Variables are passed to `Execute()`, `ExecuteAndCapture()`, or `Evaluate()`
- Method return values are used as code
- String interpolation or concatenation involves non-constant values

To suppress the warning when you intentionally use dynamic code (e.g., from a trusted configuration file):

```csharp
#pragma warning disable DOTNETPY001
executor.Execute(trustedScriptFromConfig);
#pragma warning restore DOTNETPY001
```
