using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.UnitTests;

/// <summary>
/// Locks the provider-neutral diagnostic semantics used by What's Wrong inspection.
/// 锁定 What's Wrong inspection 使用的无厂商诊断语义。
/// </summary>
public sealed class CadDiagnosticsTests
{
    [Fact]
    public void WarningDoesNotBecomeAnError()
    {
        CadInspectionSnapshot snapshot = CreateSnapshot(
            new CadDiagnostic
            {
                Code = "feature.rebuild-warning",
                Severity = CadDiagnosticSeverity.Warning,
                Message = "A rebuild warning was reported.",
                Scope = "feature",
                EntityIdentity = "document:part:feature:Boss-Extrude-1",
                NativeCode = 17,
            });

        Assert.False(snapshot.HasErrors);
    }

    [Fact]
    public void ErrorLevelDiagnosticBlocksHealthyInspectionClaim()
    {
        CadInspectionSnapshot snapshot = CreateSnapshot(
            new CadDiagnostic
            {
                Code = "feature.rebuild-error",
                Severity = CadDiagnosticSeverity.Error,
                Message = "A rebuild error was reported.",
                Scope = "feature",
                EntityIdentity = "document:part:feature:Boss-Extrude-1",
                NativeCode = 32,
            });

        Assert.True(snapshot.HasErrors);
        Assert.Single(snapshot.Diagnostics);
        Assert.Equal("feature.rebuild-error", snapshot.Diagnostics[0].Code);
    }

    [Fact]
    public void RebuildReceiptCarriesDiagnosticsInsteadOfOnlyABoolean()
    {
        CadDiagnostic diagnostic = new()
        {
            Code = "feature.rebuild-error",
            Severity = CadDiagnosticSeverity.Error,
            Message = "A rebuild error was reported.",
            Scope = "feature",
        };

        var receipt = new RebuildReceipt
        {
            StateHash = "sha256:post-rebuild",
            HasErrors = true,
            Diagnostics = [diagnostic],
        };

        Assert.True(receipt.HasErrors);
        Assert.Equal(diagnostic, Assert.Single(receipt.Diagnostics));
    }

    private static CadInspectionSnapshot CreateSnapshot(CadDiagnostic diagnostic) => new()
    {
        Document = new CadDocumentSummary
        {
            DocumentId = new DocumentId("document:part"),
            DocumentType = CadDocumentType.Part,
            Path = "C:\\isolated\\part.sldprt",
            Configuration = "Default",
            StateHash = "sha256:inspection",
            IsDirty = false,
        },
        Diagnostics = [diagnostic],
    };
}
