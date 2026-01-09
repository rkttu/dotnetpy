namespace DotNetPy.Analyzers;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// Analyzes calls to DotNetPy execution methods and warns when non-constant strings are passed
/// as the code parameter, which may indicate a potential code injection vulnerability.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CodeInjectionAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The fully qualified type name of DotNetPyExecutor.
    /// </summary>
    private const string ExecutorTypeName = "DotNetPy.DotNetPyExecutor";

    /// <summary>
    /// Methods that execute Python code and their code parameter index.
    /// </summary>
    private static readonly ImmutableDictionary<string, int> TargetMethods = new Dictionary<string, int>
    {
        ["Execute"] = 0,           // Execute(string code) or Execute(string code, Dictionary variables)
        ["ExecuteAndCapture"] = 0, // ExecuteAndCapture(string code, ...) 
        ["Evaluate"] = 0           // Evaluate(string expression)
    }.ToImmutableDictionary();

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.PotentialCodeInjection);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Get the method name
        string? methodName = GetMethodName(invocation);
        if (methodName == null || !TargetMethods.TryGetValue(methodName, out int codeParameterIndex))
        {
            return;
        }

        // Get the semantic model to verify this is actually DotNetPyExecutor
        var semanticModel = context.SemanticModel;
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, context.CancellationToken);

        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            return;
        }

        // Verify the containing type is DotNetPyExecutor
        if (methodSymbol.ContainingType?.ToDisplayString() != ExecutorTypeName)
        {
            return;
        }

        // Get the code argument
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count <= codeParameterIndex)
        {
            return;
        }

        var codeArgument = arguments[codeParameterIndex];
        var codeExpression = codeArgument.Expression;

        // Check if the argument is a constant or literal
        if (!IsConstantOrLiteral(codeExpression, semanticModel, context.CancellationToken))
        {
            var parameterName = methodSymbol.Parameters[codeParameterIndex].Name;

            var diagnostic = Diagnostic.Create(
                DiagnosticDescriptors.PotentialCodeInjection,
                codeArgument.GetLocation(),
                parameterName,
                methodName);

            context.ReportDiagnostic(diagnostic);
        }
    }

    /// <summary>
    /// Extracts the method name from an invocation expression.
    /// </summary>
    private static string? GetMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => null
        };
    }

    /// <summary>
    /// Determines whether the expression is a constant or literal string.
    /// </summary>
    private static bool IsConstantOrLiteral(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        // Check for string literals
        if (expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return true;
        }

        // Check for verbatim string literals (@"...")
        if (expression is LiteralExpressionSyntax)
        {
            return true;
        }

        // Check for raw string literals (""" """)
        if (expression.IsKind(SyntaxKind.Utf8StringLiteralExpression))
        {
            return true;
        }

        // Check for interpolated strings that only contain literals
        if (expression is InterpolatedStringExpressionSyntax interpolated)
        {
            return AreAllInterpolationsConstant(interpolated, semanticModel, cancellationToken);
        }

        // Check for constant expressions (const fields, etc.)
        var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
        if (constantValue.HasValue && constantValue.Value is string)
        {
            return true;
        }

        // Check for string concatenation of constants
        if (expression is BinaryExpressionSyntax binary &&
            binary.IsKind(SyntaxKind.AddExpression))
        {
            return IsConstantOrLiteral(binary.Left, semanticModel, cancellationToken) &&
                   IsConstantOrLiteral(binary.Right, semanticModel, cancellationToken);
        }

        // Check for parenthesized expressions
        if (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            return IsConstantOrLiteral(parenthesized.Expression, semanticModel, cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// Checks if all interpolations in an interpolated string are constant.
    /// </summary>
    private static bool AreAllInterpolationsConstant(
        InterpolatedStringExpressionSyntax interpolated,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        foreach (var content in interpolated.Contents)
        {
            if (content is InterpolationSyntax interpolation)
            {
                var constantValue = semanticModel.GetConstantValue(interpolation.Expression, cancellationToken);
                if (!constantValue.HasValue)
                {
                    return false;
                }
            }
            // InterpolatedStringTextSyntax is always constant (literal text)
        }

        return true;
    }
}
