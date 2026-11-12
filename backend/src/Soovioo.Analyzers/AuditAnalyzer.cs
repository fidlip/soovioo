using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Soovioo.Analyzers;

/// <summary>Every data-changing controller action says what it does in the
/// audit log, or that it isn't logged — a forgotten description fails the
/// build instead of showing up in Logy as a bare API path.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AuditAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "Audit";

    public static readonly DiagnosticDescriptor MissingAction = new(
        "SOOV001",
        "Data-changing action without an audit description",
        "Action '{0}' changes data but has neither [AuditAction(\"…\")] nor [NoAudit]",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingArea = new(
        "SOOV002",
        "Audited controller without an agenda",
        "Controller '{0}' has audited actions but no [AuditArea(\"…\")]",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EmptyText = new(
        "SOOV003",
        "Empty audit text",
        "[{0}] needs a non-empty text",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Contradiction = new(
        "SOOV004",
        "Action both audited and not",
        "Action '{0}' has both [AuditAction] and [NoAudit]",
        Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    private const string ControllerBase = "Microsoft.AspNetCore.Mvc.ControllerBase";
    private const string AuditArea = "Soovioo.Api.Audit.AuditAreaAttribute";
    private const string AuditAction = "Soovioo.Api.Audit.AuditActionAttribute";
    private const string NoAudit = "Soovioo.Api.Audit.NoAuditAttribute";

    private static readonly ImmutableHashSet<string> WriteVerbs = ImmutableHashSet.Create(
        "Microsoft.AspNetCore.Mvc.HttpPostAttribute",
        "Microsoft.AspNetCore.Mvc.HttpPutAttribute",
        "Microsoft.AspNetCore.Mvc.HttpPatchAttribute",
        "Microsoft.AspNetCore.Mvc.HttpDeleteAttribute");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(MissingAction, MissingArea, EmptyText, Contradiction);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || !DerivesFrom(type, ControllerBase))
        {
            return;
        }

        var audited = false;
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.MethodKind != MethodKind.Ordinary || method.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }
            var attributes = method.GetAttributes();
            if (!attributes.Any(a => FullName(a.AttributeClass) is { } name && WriteVerbs.Contains(name)))
            {
                continue;
            }
            var action = attributes.FirstOrDefault(a => FullName(a.AttributeClass) == AuditAction);
            var noAudit = attributes.Any(a => FullName(a.AttributeClass) == NoAudit);
            var location = method.Locations.FirstOrDefault();
            if (action is null && !noAudit)
            {
                context.ReportDiagnostic(Diagnostic.Create(MissingAction, location, method.Name));
            }
            else if (action is not null && noAudit)
            {
                context.ReportDiagnostic(Diagnostic.Create(Contradiction, location, method.Name));
            }
            if (action is not null)
            {
                audited = true;
                ReportIfEmpty(context, action, "AuditAction", location);
            }
        }

        var area = AreaOf(type);
        if (area is not null)
        {
            ReportIfEmpty(context, area, "AuditArea", type.Locations.FirstOrDefault());
        }
        else if (audited)
        {
            context.ReportDiagnostic(Diagnostic.Create(MissingArea, type.Locations.FirstOrDefault(), type.Name));
        }
    }

    private static AttributeData? AreaOf(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var area = current.GetAttributes().FirstOrDefault(a => FullName(a.AttributeClass) == AuditArea);
            if (area is not null)
            {
                return area;
            }
        }
        return null;
    }

    private static void ReportIfEmpty(SymbolAnalysisContext context, AttributeData attribute, string name, Location? fallback)
    {
        var text = attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as string : null;
        if (string.IsNullOrWhiteSpace(text))
        {
            var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? fallback;
            context.ReportDiagnostic(Diagnostic.Create(EmptyText, location, name));
        }
    }

    private static bool DerivesFrom(INamedTypeSymbol type, string baseName)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (FullName(current) == baseName)
            {
                return true;
            }
        }
        return false;
    }

    private static string? FullName(INamedTypeSymbol? type) =>
        type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted));
}
