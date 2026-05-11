using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

// Native AOT consumer sample.
//
// Drives the AOT-compiled DotNetPy.Native.Shared DLL through its C exports the
// way a real C/C++/Rust consumer would. The .NET host process is only a
// convenience for P/Invoke; the DLL itself never sees it.
//
// Workflow:
//   1. Publish the native shared library (one-time setup):
//        dotnet publish src/DotNetPy.Native.Shared/DotNetPy.Native.Shared.csproj \
//            --configuration Release --runtime win-x64
//      On Windows this requires the Visual Studio C++ toolchain (link.exe) on
//      PATH for the AOT compiler to produce the final DLL.
//
//   2. Run this sample:
//        cd samples/native-aot
//        dotnet run -- <python-shared-library>
//      e.g.
//        dotnet run -- $env:APPDATA\uv\python\cpython-3.13.13+freethreaded-windows-x86_64-none\python313t.dll
//
//   3. Optional: override the AOT DLL location via either a second positional
//      arg or the DOTNETPY_NATIVE_DLL environment variable. By default the
//      sample walks up from its build output looking for the conventional
//      publish path under src/DotNetPy.Native.Shared/.

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: DotNetPy.NativeAotConsumer <python-library> [<aot-dll>]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  <python-library>  Path to a CPython shared library (python313.dll, python313t.dll, ...).");
    Console.Error.WriteLine("  <aot-dll>         Optional path to dotnetpy-native.dll. Defaults to the conventional");
    Console.Error.WriteLine("                    publish location, or DOTNETPY_NATIVE_DLL if set.");
    return 2;
}

string pythonLib = args[0];
if (!File.Exists(pythonLib))
{
    Console.Error.WriteLine($"Python library not found: {pythonLib}");
    return 2;
}

string? aotOverride = args.Length >= 2 ? args[1] : Environment.GetEnvironmentVariable("DOTNETPY_NATIVE_DLL");
string aotPath = NativeApi.ResolveDllPath(aotOverride);
if (!File.Exists(aotPath))
{
    Console.Error.WriteLine($"AOT DLL not found: {aotPath}");
    Console.Error.WriteLine("Publish it first:");
    Console.Error.WriteLine("  dotnet publish src/DotNetPy.Native.Shared/DotNetPy.Native.Shared.csproj \\");
    Console.Error.WriteLine("      --configuration Release --runtime win-x64");
    return 2;
}
NativeApi.RegisterResolver(aotPath);

Console.WriteLine($"AOT DLL : {aotPath}");
Console.WriteLine($"Python  : {pythonLib}");
Console.WriteLine();

int initRc = NativeApi.Initialize(pythonLib);
if (initRc != 0)
{
    Console.Error.WriteLine($"dotnetpy_initialize failed: {initRc}");
    return 3;
}
Console.WriteLine("[ok] dotnetpy_initialize");

int totalFailures = 0;

void Check(string label, Func<bool> ok)
{
    if (ok())
    {
        Console.WriteLine($"[ok] {label}");
    }
    else
    {
        Console.WriteLine($"[FAIL] {label}");
        totalFailures++;
    }
}

// Smoke #1 — evaluate a literal expression.
Check("dotnetpy_evaluate '1+1' => 2", () =>
{
    string? r = NativeApi.Evaluate("1 + 1");
    return r == "2";
});

// Smoke #2 — execute, then capture a variable.
Check("execute 'x = 7' + capture_variable 'x' => 7", () =>
{
    NativeApi.Clear();
    int rc = NativeApi.Execute("x = 7");
    if (rc != 0) return false;

    if (NativeApi.VariableExists("x") != 1) return false;
    string? captured = NativeApi.CaptureVariable("x");
    return captured == "7";
});

// Smoke #3 — execute_and_capture with a JSON-shaped result.
Check("execute_and_capture dict => json", () =>
{
    NativeApi.Clear();
    string code = """
import math
result = {"sqrt": math.sqrt(16), "answer": 42}
""";
    string? r = NativeApi.ExecuteAndCapture(code);
    if (r == null) { Console.Error.WriteLine("        got null"); return false; }
    bool ok = r.Contains("\"answer\": 42") && r.Contains("\"sqrt\": 4");
    if (!ok) Console.Error.WriteLine($"        got: {r}");
    return ok;
});

// Smoke #4 — delete_variable round-trips correctly.
Check("delete_variable existing => 1, missing => 0", () =>
{
    NativeApi.Clear();
    NativeApi.Execute("doomed = 1");
    int del1 = NativeApi.DeleteVariable("doomed");
    int del2 = NativeApi.DeleteVariable("doomed");
    return del1 == 1 && del2 == 0;
});

// Smoke #5 — clear_globals wipes user variables.
Check("clear_globals removes user vars", () =>
{
    NativeApi.Execute("a = 1; b = 2");
    NativeApi.Clear();
    return NativeApi.VariableExists("a") == 0 && NativeApi.VariableExists("b") == 0;
});

// Concurrency #1 — many parallel execute_and_capture calls with caller-unique
// user variables. Exercises DotNetPy's internal scratch-name isolation through
// the AOT-compiled code path.
Check("parallel execute_and_capture (caller-unique vars)", () =>
{
    const int callerCount = 16;
    const int iterationsPerCaller = 8;
    var failures = new ConcurrentBag<string>();

    var tasks = Enumerable.Range(0, callerCount).Select(callerId => Task.Run(() =>
    {
        string seedVar = $"seed_caller_{callerId}";
        string resultVar = $"result_caller_{callerId}";
        for (int i = 0; i < iterationsPerCaller; i++)
        {
            int seed = callerId * 1000 + i;
            int expected = seed * 2 + 1;
            string code = $"{seedVar} = {seed}\n{resultVar} = {seedVar} * 2 + 1";
            NativeApi.Execute(code);
            string? got = NativeApi.CaptureVariable(resultVar);
            if (got != expected.ToString())
                failures.Add($"caller {callerId} iter {i}: expected {expected}, got {got ?? "null"}");
        }
    })).ToArray();
    Task.WaitAll(tasks);

    foreach (var f in failures.Take(5))
        Console.Error.WriteLine($"        {f}");
    return failures.IsEmpty;
});

// Concurrency #2 — interleaved evaluate calls. Each caller uses a distinct
// constant so there is no shared user state. The only contention is on
// DotNetPy's own internal scratch slots.
Check("parallel evaluate (no shared user state)", () =>
{
    const int callerCount = 32;
    const int iterationsPerCaller = 16;
    var failures = new ConcurrentBag<string>();

    var tasks = Enumerable.Range(0, callerCount).Select(callerId => Task.Run(() =>
    {
        for (int i = 0; i < iterationsPerCaller; i++)
        {
            int n = callerId * 1000 + i;
            int expected = n * n;
            string? got = NativeApi.Evaluate($"{n} * {n}");
            if (got != expected.ToString())
                failures.Add($"caller {callerId} iter {i}: expected {expected}, got {got ?? "null"}");
        }
    })).ToArray();
    Task.WaitAll(tasks);

    foreach (var f in failures.Take(5))
        Console.Error.WriteLine($"        {f}");
    return failures.IsEmpty;
});

Console.WriteLine();
Console.WriteLine(totalFailures == 0
    ? "PASS — all native C-API smoke checks succeeded."
    : $"FAIL — {totalFailures} check(s) failed.");

return totalFailures == 0 ? 0 : 1;


/// <summary>
/// P/Invoke surface for the AOT-published <c>dotnetpy-native.dll</c>.
/// The DLL is located at runtime via <see cref="NativeLibrary.SetDllImportResolver"/>
/// so this sample is not tied to any specific build output path.
/// </summary>
internal static unsafe class NativeApi
{
    private const string NativeLibName = "dotnetpy-native";
    private static string _resolvedPath = string.Empty;

    public static void RegisterResolver(string dllPath)
    {
        _resolvedPath = dllPath;
        NativeLibrary.SetDllImportResolver(typeof(NativeApi).Assembly, ResolveImport);
    }

    private static IntPtr ResolveImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        => string.Equals(libraryName, NativeLibName, StringComparison.Ordinal)
            ? NativeLibrary.Load(_resolvedPath)
            : IntPtr.Zero;

    public static string ResolveDllPath(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;

        // Walk up from the assembly's directory looking for the conventional
        // publish location: src/DotNetPy.Native.Shared/bin/Release/net8.0/<rid>/publish/dotnetpy-native.dll
        string? dir = Path.GetDirectoryName(typeof(NativeApi).Assembly.Location);
        string ridGuess = RuntimeInformation.RuntimeIdentifier; // e.g. win-x64
        string conventionalSuffix = Path.Combine(
            "src", "DotNetPy.Native.Shared",
            "bin", "Release", "net8.0", ridGuess, "publish", "dotnetpy-native.dll");

        while (!string.IsNullOrEmpty(dir))
        {
            string candidate = Path.Combine(dir, conventionalSuffix);
            if (File.Exists(candidate))
                return candidate;
            string? parent = Path.GetDirectoryName(dir);
            if (parent == dir) break;
            dir = parent;
        }

        // Last resort: return the conventional path relative to the working
        // directory. The caller will check File.Exists and report a useful error.
        return Path.Combine(Directory.GetCurrentDirectory(), conventionalSuffix);
    }

    [DllImport(NativeLibName, EntryPoint = "dotnetpy_initialize", CallingConvention = CallingConvention.Cdecl)]
    private static extern int dotnetpy_initialize(byte* libraryPath);

    [DllImport(NativeLibName, EntryPoint = "dotnetpy_execute", CallingConvention = CallingConvention.Cdecl)]
    private static extern int dotnetpy_execute(byte* code);

    [DllImport(NativeLibName, EntryPoint = "dotnetpy_evaluate", CallingConvention = CallingConvention.Cdecl)]
    private static extern int dotnetpy_evaluate(byte* expression, byte* resultBuffer, int bufferSize);

    [DllImport(NativeLibName, EntryPoint = "dotnetpy_execute_and_capture", CallingConvention = CallingConvention.Cdecl)]
    private static extern int dotnetpy_execute_and_capture(byte* code, byte* resultBuffer, int bufferSize);

    [DllImport(NativeLibName, EntryPoint = "dotnetpy_variable_exists", CallingConvention = CallingConvention.Cdecl)]
    private static extern int dotnetpy_variable_exists(byte* variableName);

    [DllImport(NativeLibName, EntryPoint = "dotnetpy_capture_variable", CallingConvention = CallingConvention.Cdecl)]
    private static extern int dotnetpy_capture_variable(byte* variableName, byte* resultBuffer, int bufferSize);

    [DllImport(NativeLibName, EntryPoint = "dotnetpy_delete_variable", CallingConvention = CallingConvention.Cdecl)]
    private static extern int dotnetpy_delete_variable(byte* variableName);

    [DllImport(NativeLibName, EntryPoint = "dotnetpy_clear_globals", CallingConvention = CallingConvention.Cdecl)]
    private static extern int dotnetpy_clear_globals();

    public static int Initialize(string libraryPath)
    {
        var bytes = Encoding.UTF8.GetBytes(libraryPath + "\0");
        fixed (byte* p = bytes) return dotnetpy_initialize(p);
    }

    public static int Execute(string code)
    {
        var bytes = Encoding.UTF8.GetBytes(code + "\0");
        fixed (byte* p = bytes) return dotnetpy_execute(p);
    }

    public static int DeleteVariable(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes) return dotnetpy_delete_variable(p);
    }

    public static int VariableExists(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes) return dotnetpy_variable_exists(p);
    }

    public static int Clear() => dotnetpy_clear_globals();

    public static string? Evaluate(string expr)
    {
        var inb = Encoding.UTF8.GetBytes(expr + "\0");
        var outBuf = new byte[8192];
        int rc;
        fixed (byte* pin = inb)
        fixed (byte* pout = outBuf)
        {
            rc = dotnetpy_evaluate(pin, pout, outBuf.Length);
        }
        return rc < 0 ? null : Encoding.UTF8.GetString(outBuf, 0, rc);
    }

    public static string? ExecuteAndCapture(string code)
    {
        var inb = Encoding.UTF8.GetBytes(code + "\0");
        var outBuf = new byte[16384];
        int rc;
        fixed (byte* pin = inb)
        fixed (byte* pout = outBuf)
        {
            rc = dotnetpy_execute_and_capture(pin, pout, outBuf.Length);
        }
        return rc < 0 ? null : Encoding.UTF8.GetString(outBuf, 0, rc);
    }

    public static string? CaptureVariable(string name)
    {
        var inb = Encoding.UTF8.GetBytes(name + "\0");
        var outBuf = new byte[8192];
        int rc;
        fixed (byte* pin = inb)
        fixed (byte* pout = outBuf)
        {
            rc = dotnetpy_capture_variable(pin, pout, outBuf.Length);
        }
        return rc < 0 ? null : Encoding.UTF8.GetString(outBuf, 0, rc);
    }
}
