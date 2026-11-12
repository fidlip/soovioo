using System.Collections.Immutable;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Soovioo.Analyzers;
using Soovioo.Api.Audit;

namespace Soovioo.Api.Tests.Analyzers;

public class AuditAnalyzerTests
{
    private const string Usings = """
        using Microsoft.AspNetCore.Mvc;
        using Soovioo.Api.Audit;

        """;

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(ControllerBase).Assembly.Location)
            .Append(typeof(AuditActionAttribute).Assembly.Location)
            .Distinct()
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("Sample",
            [CSharpSyntaxTree.ParseText(Usings + source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        return await compilation.WithAnalyzers([new AuditAnalyzer()]).GetAnalyzerDiagnosticsAsync();
    }

    [Fact]
    public async Task DescribedAndExcludedActionsPass()
    {
        var diagnostics = await AnalyzeAsync("""
            [AuditArea("Osoby")]
            public class PeopleController : ControllerBase
            {
                [HttpGet] public IActionResult List() => Ok();
                [HttpPost] [AuditAction("Vytvořena osoba")] public IActionResult Add() => Ok();
                [HttpDelete("{id}")] [AuditAction("Smazána osoba")] public IActionResult Delete(int id) => Ok();
                [HttpPost("read")] [NoAudit] public IActionResult MarkRead() => Ok();
            }

            public class DevicesController : ControllerBase
            {
                [HttpPost] [NoAudit] public IActionResult Register() => Ok();
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task AWriteActionWithoutDescriptionFails()
    {
        var diagnostics = await AnalyzeAsync("""
            [AuditArea("Osoby")]
            public class PeopleController : ControllerBase
            {
                [HttpPut("{id}")] public IActionResult Update(int id) => Ok();
                [HttpPatch("{id}")] public IActionResult Patch(int id) => Ok();
                [HttpGet] public IActionResult List() => Ok();
            }
            """);

        Assert.Equal(["SOOV001", "SOOV001"], diagnostics.Select(d => d.Id));
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.Equal(["Update", "Patch"], diagnostics.Select(d => d.Location.SourceTree!.GetText()
            .ToString(d.Location.SourceSpan)));
    }

    [Fact]
    public async Task AnAuditedControllerWithoutAgendaFails()
    {
        var diagnostics = await AnalyzeAsync("""
            public class PeopleController : ControllerBase
            {
                [HttpPost] [AuditAction("Vytvořena osoba")] public IActionResult Add() => Ok();
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("SOOV002", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public async Task AnAgendaOnABaseControllerCounts()
    {
        var diagnostics = await AnalyzeAsync("""
            [AuditArea("Rozvrh")]
            public abstract class TimetableControllerBase : ControllerBase;

            public class BoardController : TimetableControllerBase
            {
                [HttpPost] [AuditAction("Umístěna hodina")] public IActionResult Place() => Ok();
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task EmptyTextsAndContradictionsFail()
    {
        var diagnostics = await AnalyzeAsync("""
            [AuditArea(" ")]
            public class PeopleController : ControllerBase
            {
                [HttpPost] [AuditAction("")] public IActionResult Add() => Ok();
                [HttpDelete] [AuditAction("Smazána osoba")] [NoAudit] public IActionResult Delete() => Ok();
            }
            """);

        Assert.Equal(["SOOV003", "SOOV003", "SOOV004"], diagnostics.Select(d => d.Id).Order());
    }
}
