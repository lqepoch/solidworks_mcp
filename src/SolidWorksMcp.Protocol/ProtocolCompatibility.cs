namespace SolidWorksMcp.Protocol;

/// <summary>Classifies whether a schema/version pair can be consumed without a breaking contract change.</summary>
/// <remarks>
/// Compatibility is checked by identity first and version second. A different schema or major version is breaking;
/// an older minor version in the same major line is backward-compatible. 先检查 schema identity，再检查版本；不同
/// schema 或主版本属于 breaking change，同一主版本下较旧 minor 版本允许向后兼容。
/// </remarks>
public sealed record CompatibilityReport
{
    /// <summary>Creates a compatibility report.</summary>
    public CompatibilityReport(bool isCompatible, string reason)
    {
        IsCompatible = isCompatible;
        Reason = string.IsNullOrWhiteSpace(reason) ? throw new ArgumentException("A reason is required.", nameof(reason)) : reason.Trim();
    }

    /// <summary>Gets whether the requested contract can be consumed.</summary>
    public bool IsCompatible { get; }

    /// <summary>Gets a deterministic explanation suitable for logs and diagnostics.</summary>
    public string Reason { get; }
}

/// <summary>Central compatibility policy for versioned MCP operation contracts.</summary>
/// <remarks>
/// This policy is intentionally strict about future versions: callers may use an older compatible contract, but a
/// newer minor version must be negotiated explicitly before use. 该策略对未来版本保持 fail-closed：调用方可以使用
/// 已知主版本下的旧兼容版本，但未知的更新 minor 版本必须先显式协商。
/// </remarks>
public static class ProtocolCompatibility
{
    /// <summary>Checks the operation-result schema and version against the current policy.</summary>
    public static CompatibilityReport Check(string schema, string version)
    {
        if (!string.Equals(schema, ProtocolSchema.OperationResult, StringComparison.Ordinal))
        {
            return new CompatibilityReport(false, "The operation-result schema identifier is not supported.");
        }

        if (!TryParseVersion(version, out int major, out int minor))
        {
            return new CompatibilityReport(false, "The schema version must use major.minor numeric form.");
        }

        if (major != ProtocolSchema.CurrentMajor)
        {
            return new CompatibilityReport(false, "A different major schema version requires an explicit migration.");
        }

        if (minor > ProtocolSchema.CurrentMinor)
        {
            return new CompatibilityReport(false, "A future minor schema version requires explicit capability negotiation.");
        }

        return new CompatibilityReport(true, "The schema is backward-compatible with the current operation contract.");
    }

    /// <summary>Returns true only for a schema/version pair accepted by <see cref="Check"/>.</summary>
    public static bool IsSupported(string schema, string version) => Check(schema, version).IsCompatible;

    private static bool TryParseVersion(string? version, out int major, out int minor)
    {
        major = 0;
        minor = 0;
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        string[] parts = version.Trim().Split('.', StringSplitOptions.None);
        return parts.Length == 2
            && int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out major)
            && int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out minor)
            && major >= 0
            && minor >= 0;
    }
}
