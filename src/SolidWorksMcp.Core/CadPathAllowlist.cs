using System.Collections.Immutable;

namespace SolidWorksMcp.Core;

/// <summary>Result of validating one persisted CAD target against the operator's path policy.</summary>
/// <remarks>
/// This result contains the normalized path only for the trusted provider boundary; MCP discovery never serializes it.
/// 该结果只把规范化路径交给受信任的 Provider 边界；MCP capability discovery 不会序列化此路径。
/// </remarks>
public sealed record CadPathValidationResult
{
    /// <summary>Gets whether the target can be used by the requested operation.</summary>
    public bool IsAllowed { get; init; }

    /// <summary>Gets the normalized absolute path when validation succeeds.</summary>
    public string? FullPath { get; init; }

    /// <summary>Gets a stable diagnostic reason without echoing the caller's raw path.</summary>
    public string? FailureReason { get; init; }
}

/// <summary>
/// Immutable path allowlist for CAD artifacts created by the MCP runtime.
/// MCP runtime 创建 CAD artifact 使用的不可变路径白名单。
/// </summary>
/// <remarks>
/// The policy is intentionally deny-by-default: an empty root set rejects every create target. It performs lexical
/// canonicalization and a path-separator boundary check, preventing <c>C:\work</c> from matching <c>C:\work-old</c>.
/// It does not claim to resolve filesystem reparse points; deployment guidance must keep approved roots free of
/// untrusted junctions/symlinks until a handle-based Windows canonicalization layer is added.
/// 策略刻意默认拒绝：空根目录集合会拒绝所有 create target。它执行 lexical canonicalization 和路径分隔符边界
/// 检查，避免 <c>C:\work</c> 错误匹配 <c>C:\work-old</c>。它暂不声称解析 filesystem reparse point；在基于句柄
/// 的 Windows canonicalization 层加入前，部署文档应要求 allowlisted root 不包含不可信 junction/symlink。
/// </remarks>
public sealed class CadPathAllowlist
{
    /// <summary>Creates a policy from operator-selected absolute directory roots.</summary>
    /// <param name="allowedRoots">Approved directories; an empty sequence is a valid fail-closed policy.</param>
    public CadPathAllowlist(IEnumerable<string> allowedRoots)
    {
        ArgumentNullException.ThrowIfNull(allowedRoots);

        var normalized = new List<string>();
        foreach (string root in allowedRoots)
        {
            if (string.IsNullOrWhiteSpace(root)
                || root.Contains('\r')
                || root.Contains('\n')
                || !Path.IsPathRooted(root.Trim()))
            {
                throw new ArgumentException("Path allowlist roots must be non-empty absolute paths.", nameof(allowedRoots));
            }

            string fullRoot;
            try
            {
                fullRoot = Path.GetFullPath(root.Trim());
            }
            catch (ArgumentException exception)
            {
                throw new ArgumentException("Path allowlist contains an invalid root.", nameof(allowedRoots), exception);
            }

            if (!Directory.Exists(fullRoot))
            {
                throw new ArgumentException("Path allowlist roots must already exist as directories.", nameof(allowedRoots));
            }

            if (!normalized.Contains(fullRoot, StringComparer.OrdinalIgnoreCase))
            {
                normalized.Add(fullRoot);
            }
        }

        AllowedRoots = [.. normalized.Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Gets normalized approved roots in deterministic order.</summary>
    public ImmutableArray<string> AllowedRoots { get; }

    /// <summary>Returns a fail-closed policy with no approved roots.</summary>
    public static CadPathAllowlist DenyAll { get; } = new([]);

    /// <summary>Validates a new persisted target without creating directories or touching its contents.</summary>
    /// <param name="rawPath">Explicit absolute path supplied by a trusted caller.</param>
    /// <param name="requiredExtension">Required extension, such as <c>.sldprt</c>.</param>
    public CadPathValidationResult ValidateCreateTarget(string? rawPath, string requiredExtension)
    {
        if (string.IsNullOrWhiteSpace(rawPath)
            || rawPath.Contains('\r')
            || rawPath.Contains('\n'))
        {
            return Denied("missing-or-invalid-path");
        }

        string trimmedPath = rawPath.Trim();
        if (!Path.IsPathRooted(trimmedPath))
        {
            return Denied("relative-path");
        }

        if (string.IsNullOrWhiteSpace(requiredExtension))
        {
            return Denied("missing-required-extension");
        }

        string extension = requiredExtension.Trim();
        if (extension[0] != '.')
        {
            extension = $".{extension}";
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(trimmedPath);
        }
        catch (ArgumentException)
        {
            return Denied("invalid-path");
        }

        if (!fullPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            return Denied("wrong-extension");
        }

        if (AllowedRoots.IsDefaultOrEmpty || !AllowedRoots.Any(root => IsWithinRoot(root, fullPath)))
        {
            return Denied("outside-allowlist");
        }

        string? parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
        {
            return Denied("parent-directory-missing");
        }

        return new CadPathValidationResult { IsAllowed = true, FullPath = fullPath };
    }

    /// <summary>Checks containment with a separator boundary, not a naive string prefix.</summary>
    private static bool IsWithinRoot(string root, string candidate)
    {
        if (root.Equals(candidate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string separator = root.EndsWith(Path.DirectorySeparatorChar)
            || root.EndsWith(Path.AltDirectorySeparatorChar)
            ? string.Empty
            : Path.DirectorySeparatorChar.ToString();
        return candidate.StartsWith(root + separator, StringComparison.OrdinalIgnoreCase);
    }

    private static CadPathValidationResult Denied(string reason) =>
        new() { IsAllowed = false, FailureReason = reason };
}
