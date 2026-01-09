namespace DotNetPy.Analyzers.Tests;

using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

public class CodeInjectionAnalyzerTests
{
    /// <summary>
    /// Helper method to create and run the analyzer test.
    /// </summary>
    private static async Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<CodeInjectionAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        // Add DotNetPy mock types for testing
        test.TestState.Sources.Add(DotNetPyMockTypes);

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    /// <summary>
    /// Mock implementation of DotNetPyExecutor for testing purposes.
    /// </summary>
    private const string DotNetPyMockTypes = @"
namespace DotNetPy
{
    public class DotNetPyExecutor
    {
        public void Execute(string code) { }
        public void Execute(string code, System.Collections.Generic.Dictionary<string, object?> variables) { }
        public object? ExecuteAndCapture(string code, string resultVariable = ""result"") => null;
        public object? ExecuteAndCapture(string code, System.Collections.Generic.Dictionary<string, object?> variables, string resultVariable = ""result"") => null;
        public object? Evaluate(string expression) => null;
    }
}
";

    [Fact]
    public async Task StringLiteral_NoDiagnostic()
    {
        var source = @"
using DotNetPy;

class Test
{
    void Method()
    {
        var executor = new DotNetPyExecutor();
        executor.Execute(""print('hello')"");
    }
}";
        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task VerbatimStringLiteral_NoDiagnostic()
    {
        var source = @"
using DotNetPy;

class Test
{
    void Method()
    {
        var executor = new DotNetPyExecutor();
        executor.Execute(@""
            import math
            result = math.sqrt(16)
        "");
    }
}";
        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task ConstField_NoDiagnostic()
    {
        var source = @"
using DotNetPy;

class Test
{
    private const string PythonCode = ""print('hello')"";

    void Method()
    {
        var executor = new DotNetPyExecutor();
        executor.Execute(PythonCode);
    }
}";
        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Variable_ReportsDiagnostic()
    {
        var source = @"
using DotNetPy;

class Test
{
    void Method(string userInput)
    {
        var executor = new DotNetPyExecutor();
        executor.Execute({|#0:userInput|});
    }
}";
        var expected = new DiagnosticResult("DOTNETPY001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("code", "Execute");

        await VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task MethodReturnValue_ReportsDiagnostic()
    {
        var source = @"
using DotNetPy;

class Test
{
    string GetScript() => ""print('hello')"";

    void Method()
    {
        var executor = new DotNetPyExecutor();
        executor.Execute({|#0:GetScript()|});
    }
}";
        var expected = new DiagnosticResult("DOTNETPY001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("code", "Execute");

        await VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task StringConcatenationWithVariable_ReportsDiagnostic()
    {
        var source = @"
using DotNetPy;

class Test
{
    void Method(string tableName)
    {
        var executor = new DotNetPyExecutor();
        executor.Execute({|#0:""SELECT * FROM "" + tableName|});
    }
}";
        var expected = new DiagnosticResult("DOTNETPY001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("code", "Execute");

        await VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task StringConcatenationOfConstants_NoDiagnostic()
    {
        var source = @"
using DotNetPy;

class Test
{
    void Method()
    {
        var executor = new DotNetPyExecutor();
        executor.Execute(""print("" + ""'hello')"");
    }
}";
        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task InterpolatedStringWithVariable_ReportsDiagnostic()
    {
        var source = @"
using DotNetPy;

class Test
{
    void Method(string varName)
    {
        var executor = new DotNetPyExecutor();
        executor.Execute({|#0:$""result = {varName}""|});
    }
}";
        var expected = new DiagnosticResult("DOTNETPY001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("code", "Execute");

        await VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task InterpolatedStringWithConstant_NoDiagnostic()
    {
        var source = @"
using DotNetPy;

class Test
{
    void Method()
    {
        const int value = 42;
        var executor = new DotNetPyExecutor();
        executor.Execute($""result = {value}"");
    }
}";
        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task Evaluate_Variable_ReportsDiagnostic()
    {
        var source = @"
using DotNetPy;

class Test
{
    void Method(string expr)
    {
        var executor = new DotNetPyExecutor();
        executor.Evaluate({|#0:expr|});
    }
}";
        var expected = new DiagnosticResult("DOTNETPY001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("expression", "Evaluate");

        await VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task ExecuteAndCapture_Variable_ReportsDiagnostic()
    {
        var source = @"
using DotNetPy;

class Test
{
    void Method(string script)
    {
        var executor = new DotNetPyExecutor();
        executor.ExecuteAndCapture({|#0:script|});
    }
}";
        var expected = new DiagnosticResult("DOTNETPY001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("code", "ExecuteAndCapture");

        await VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task ExecuteWithVariables_ConstantCode_NoDiagnostic()
    {
        var source = @"
using DotNetPy;
using System.Collections.Generic;

class Test
{
    void Method(int[] userNumbers)
    {
        var executor = new DotNetPyExecutor();
        executor.Execute(@""
            result = sum(numbers) / len(numbers)
        "", new Dictionary<string, object?> { { ""numbers"", userNumbers } });
    }
}";
        await VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task ExecuteWithVariables_VariableCode_ReportsDiagnostic()
    {
        var source = @"
using DotNetPy;
using System.Collections.Generic;

class Test
{
    void Method(string userScript, int[] userNumbers)
    {
        var executor = new DotNetPyExecutor();
        executor.Execute({|#0:userScript|}, new Dictionary<string, object?> { { ""numbers"", userNumbers } });
    }
}";
        var expected = new DiagnosticResult("DOTNETPY001", DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("code", "Execute");

        await VerifyAnalyzerAsync(source, expected);
    }
}
