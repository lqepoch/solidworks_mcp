using System.Collections.Immutable;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.AutoDrawing;

/// <summary>
/// Verified result of one targeted drawing-repair transaction.
/// 一次定向工程图修复事务的已验证结果。
/// </summary>
/// <remarks>
/// The result deliberately carries the initial and final state hashes plus the persisted reopen inspection. A caller
/// therefore cannot mistake a successful COM setter return for a durable drawing repair. 结果显式携带初始/最终 state
/// hash 以及保存重开后的 inspection，调用方不能把 COM setter 返回值误认为持久化修复成功。
/// </remarks>
public sealed record DrawingRepairExecutionResult
{
    /// <summary>Exact drawing identity inspected before and after the repair.</summary>
    public required DocumentId DocumentId { get; init; }

    /// <summary>State hash captured before the first targeted mutation.</summary>
    public required string InitialStateHash { get; init; }

    /// <summary>State hash proven after save/reopen.</summary>
    public required string FinalStateHash { get; init; }

    /// <summary>Caller-provided planner fingerprint retained for audit correlation.</summary>
    public required string RepairPlanFingerprint { get; init; }

    /// <summary>Only the exact annotation actions materialized by the provider.</summary>
    public ImmutableArray<DrawingRepairReceipt> Repairs { get; init; } = [];

    /// <summary>Save proof returned by the provider.</summary>
    public required SaveReceipt Save { get; init; }

    /// <summary>Inspection after persistence round-trip.</summary>
    public required CadInspectionSnapshot ReopenedDrawing { get; init; }
}
