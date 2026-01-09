namespace DotNetPy.Analyzers;

using Microsoft.CodeAnalysis;
using System;
using System.Globalization;

/// <summary>
/// Contains all diagnostic descriptors for DotNetPy analyzers.
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string Category = "Security";

    /// <summary>
    /// DOTNETPY001: Potential code injection vulnerability.
    /// Triggered when a non-constant string is passed to Execute, ExecuteAndCapture, or Evaluate methods.
    /// </summary>
    public static readonly DiagnosticDescriptor PotentialCodeInjection = new(
        id: "DOTNETPY001",
        title: new LocalizableMessage(
            "Potential Python code injection",
            "Python 코드 삽입 가능성"),
        messageFormat: new LocalizableMessage(
            "The '{0}' parameter passed to '{1}' is not a constant or literal string. Passing untrusted input may result in remote code execution (RCE) vulnerabilities.",
            "'{1}'에 전달된 '{0}' 매개변수가 상수 또는 리터럴 문자열이 아닙니다. 신뢰할 수 없는 입력을 전달하면 원격 코드 실행(RCE) 취약점이 발생할 수 있습니다."),
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableMessage(
            "DotNetPy executes arbitrary Python code with the same privileges as the host .NET process. Never pass untrusted or user-provided input to code execution methods. Use the 'variables' parameter to safely pass user data instead of constructing code strings dynamically.",
            "DotNetPy는 호스트 .NET 프로세스와 동일한 권한으로 임의의 Python 코드를 실행합니다. 코드 실행 메서드에 신뢰할 수 없거나 사용자가 제공한 입력을 전달하지 마세요. 동적으로 코드 문자열을 구성하는 대신 'variables' 매개변수를 사용하여 사용자 데이터를 안전하게 전달하세요."),
        helpLinkUri: "https://github.com/rkttu/dotnetpy#security-considerations");
}

/// <summary>
/// A simple localizable string implementation that supports English and Korean.
/// </summary>
internal sealed class LocalizableMessage : LocalizableString
{
    private readonly string _english;
    private readonly string _korean;

    public LocalizableMessage(string english, string korean)
    {
        _english = english;
        _korean = korean;
    }

    protected override string GetText(IFormatProvider? formatProvider)
    {
        if (formatProvider is CultureInfo culture)
        {
            if (culture.Name.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
            {
                return _korean;
            }
        }
        return _english;
    }

    protected override bool AreEqual(object? other)
    {
        return other is LocalizableMessage msg &&
               _english == msg._english &&
               _korean == msg._korean;
    }

    protected override int GetHash()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + (_english?.GetHashCode() ?? 0);
            hash = hash * 31 + (_korean?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
