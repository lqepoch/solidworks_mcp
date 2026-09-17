using SolidWorksMcp.CadAbstractions;

namespace SolidWorksMcp.AutoDrawing;

/// <summary>
/// Read-only result of the public drawing validation workflow.
/// 公开 drawing validation workflow 的只读结果；它不会修改 CAD 文档。
/// </summary>
public sealed record PartDrawingValidationResult
{
    /// <summary>Result schema version.</summary>
    public const string SchemaVersion = "1.0";

    /// <summary>Inspection proof for the exact registered drawing document.</summary>
    public required CadInspectionSnapshot Drawing { get; init; }

    /// <summary>Feature-level dimension plan and coverage findings.</summary>
    public required PartDrawingDimensionPlan DimensionPlan { get; init; }
}
