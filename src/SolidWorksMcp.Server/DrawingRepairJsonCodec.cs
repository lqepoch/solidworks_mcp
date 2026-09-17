using System.Collections.Immutable;
using System.Text.Json;
using SolidWorksMcp.AutoDrawing;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Server;

/// <summary>
/// Bounded, provider-neutral decoder for a targeted drawing repair plan.
/// 定向工程图修复计划使用有界、无厂商依赖的 decoder。
/// </summary>
/// <remarks>
/// The MCP boundary accepts only the small action vocabulary currently proved by the native provider. It does not
/// deserialize arbitrary domain objects, paths, COM values or executable expressions. MCP boundary 只接受当前 native
/// Provider 已证明的小 action vocabulary，不反序列化任意 domain object、路径、COM 值或可执行表达式。
/// </remarks>
internal static class DrawingRepairJsonCodec
{
    private const int MaxJsonCharacters = 64 * 1024;
    private const int MaxActions = 32;
    private const int MaxIdentityLength = 256;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 8,
    };

    /// <summary>Decodes and validates one repair plan before a CAD session starts.</summary>
    public static bool TryParse(
        string? json,
        out DrawingRepairPlanInput? plan,
        out string? error)
    {
        plan = null;
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "repairPlanJson is required.";
            return false;
        }

        if (json.Length > MaxJsonCharacters)
        {
            error = $"repairPlanJson cannot exceed {MaxJsonCharacters} characters.";
            return false;
        }

        RepairPlanDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<RepairPlanDto>(json, Options);
        }
        catch (JsonException exception)
        {
            error = $"repairPlanJson is malformed: {exception.Message}";
            return false;
        }

        if (dto is null)
        {
            error = "repairPlanJson deserialized to null.";
            return false;
        }

        if (!string.Equals(dto.SchemaVersion, DrawingRepairPlan.SchemaVersion, StringComparison.Ordinal))
        {
            error = $"repairPlanJson schemaVersion must be '{DrawingRepairPlan.SchemaVersion}'.";
            return false;
        }

        if (!RequiredIdentity(dto.Fingerprint, "fingerprint", out error))
        {
            return false;
        }

        if (dto.Actions is null || dto.Actions.Count == 0 || dto.Actions.Count > MaxActions)
        {
            error = $"repairPlanJson requires 1..{MaxActions} actions.";
            return false;
        }

        var actions = ImmutableArray.CreateBuilder<DrawingRepairInputAction>(dto.Actions.Count);
        foreach (RepairActionDto? action in dto.Actions)
        {
            if (action is null)
            {
                error = "repairPlanJson actions cannot contain null values.";
                return false;
            }

            if (!string.Equals(action.ActionCode, "layout.apply-planned-position", StringComparison.Ordinal))
            {
                error = "Only the proved layout.apply-planned-position repair action is currently supported.";
                return false;
            }

            if (!RequiredIdentity(action.TargetId, "targetId", out error)
                || !RequiredIdentity(action.FindingCode, "findingCode", out error)
                || !RequiredIdentity(action.PreconditionFingerprint, "preconditionFingerprint", out error))
            {
                return false;
            }

            if (action.TargetId!.Length > MaxIdentityLength
                || action.FindingCode!.Length > MaxIdentityLength
                || action.PreconditionFingerprint!.Length > MaxIdentityLength)
            {
                error = "Repair action identities exceed the bounded length policy.";
                return false;
            }

            if (action.NewPositionXMillimeters is not double x
                || action.NewPositionYMillimeters is not double y
                || !double.IsFinite(x)
                || !double.IsFinite(y))
            {
                error = "Every repair action requires finite newPositionXMillimeters and newPositionYMillimeters.";
                return false;
            }

            actions.Add(
                new DrawingRepairInputAction
                {
                    ActionCode = action.ActionCode!.Trim(),
                    TargetId = action.TargetId.Trim(),
                    FindingCode = action.FindingCode.Trim(),
                    PreconditionFingerprint = action.PreconditionFingerprint.Trim(),
                    NewPosition = new Coordinate2D(Length.FromMillimeters(x), Length.FromMillimeters(y)),
                });
        }

        if (actions.Select(action => action.TargetId).Distinct(StringComparer.Ordinal).Count() != actions.Count)
        {
            error = "Repair action target identities must be unique.";
            return false;
        }

        plan = new DrawingRepairPlanInput(dto.Fingerprint!.Trim(), actions.ToImmutable());
        return true;
    }

    private static bool RequiredIdentity(string? value, string name, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"{name} is required.";
            return false;
        }

        error = null;
        return true;
    }

    private sealed class RepairPlanDto
    {
        public string? SchemaVersion { get; set; }

        public string? Fingerprint { get; set; }

        public List<RepairActionDto>? Actions { get; set; }
    }

    private sealed class RepairActionDto
    {
        public string? ActionCode { get; set; }

        public string? TargetId { get; set; }

        public string? FindingCode { get; set; }

        public string? PreconditionFingerprint { get; set; }

        public double? NewPositionXMillimeters { get; set; }

        public double? NewPositionYMillimeters { get; set; }
    }
}

/// <summary>Validated repair input kept separate from the wire DTO.</summary>
/// <remarks>Wire JSON remains bounded and migration-friendly; the execution layer receives immutable typed values.</remarks>
internal sealed record DrawingRepairPlanInput(
    string Fingerprint,
    ImmutableArray<DrawingRepairInputAction> Actions);

/// <summary>One exact action selected by the drawing repair planner.</summary>
internal sealed record DrawingRepairInputAction
{
    public required string ActionCode { get; init; }

    public required string TargetId { get; init; }

    public required string FindingCode { get; init; }

    public required string PreconditionFingerprint { get; init; }

    public required Coordinate2D NewPosition { get; init; }
}
