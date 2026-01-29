namespace DotNetPy.Analyzers;

using Microsoft.CodeAnalysis;

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
        title: new LocalizableResourceString(
            nameof(Resources.DOTNETPY001_Title),
            Resources.ResourceManager,
            typeof(Resources)),
        messageFormat: new LocalizableResourceString(
            nameof(Resources.DOTNETPY001_MessageFormat),
            Resources.ResourceManager,
            typeof(Resources)),
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: new LocalizableResourceString(
            nameof(Resources.DOTNETPY001_Description),
            Resources.ResourceManager,
            typeof(Resources)),
        helpLinkUri: "https://github.com/rkttu/dotnetpy#security-considerations");
}
