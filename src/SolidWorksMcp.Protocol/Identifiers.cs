namespace SolidWorksMcp.Protocol;

/// <summary>Base validation for opaque identifiers that must remain stable across regeneration.</summary>
internal static class IdentifierValidation
{
    /// <summary>Rejects blank identifiers while preserving caller-selected stable text.</summary>
    public static string RequireValue(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}

/// <summary>Stable identity of a SOLIDWORKS session or fake-CAD session.</summary>
/// <remarks>Session identity prevents a request for one process from mutating another process.</remarks>
public readonly record struct SessionId
{
    /// <summary>Creates an opaque session identity.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public SessionId(string value) => Value = IdentifierValidation.RequireValue(value, nameof(value));

    /// <summary>Gets the stable serialized value.</summary>
    public string Value { get; }
}

/// <summary>Stable identity of a CAD document independent of its display title.</summary>
public readonly record struct DocumentId
{
    /// <summary>Creates an opaque document identity.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public DocumentId(string value) => Value = IdentifierValidation.RequireValue(value, nameof(value));

    /// <summary>Gets the stable serialized value.</summary>
    public string Value { get; }
}

/// <summary>Stable identity of a model feature.</summary>
public readonly record struct FeatureId
{
    /// <summary>Creates an opaque feature identity.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public FeatureId(string value) => Value = IdentifierValidation.RequireValue(value, nameof(value));

    /// <summary>Gets the stable serialized value.</summary>
    public string Value { get; }
}

/// <summary>Stable identity of a model dimension or parameter.</summary>
/// <remarks>
/// A dimension identity is separate from its rendered drawing annotation.  尺寸 identity 与工程图上的尺寸标注
/// 分离；重新生成 drawing 不得改变模型参数本身的逻辑 identity。
/// </remarks>
public readonly record struct DimensionId
{
    /// <summary>Creates an opaque dimension identity.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public DimensionId(string value) => Value = IdentifierValidation.RequireValue(value, nameof(value));

    /// <summary>Gets the stable serialized value.</summary>
    public string Value { get; }
}

/// <summary>Stable identity of an engineering requirement independent of a CAD feature or drawing annotation.</summary>
/// <remarks>
/// A requirement remains the same logical object when its model feature, view or annotation is regenerated.  工程要求的
/// identity 独立于 CAD feature 和 drawing annotation；重新生成模型或图纸不能因此创建一个新的逻辑要求。
/// </remarks>
public readonly record struct EngineeringRequirementId
{
    /// <summary>Creates an opaque engineering-requirement identity.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public EngineeringRequirementId(string value) => Value = IdentifierValidation.RequireValue(value, nameof(value));

    /// <summary>Gets the stable serialized value.</summary>
    public string Value { get; }
}

/// <summary>Stable identity of a solid body.</summary>
public readonly record struct BodyId
{
    /// <summary>Creates an opaque body identity.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public BodyId(string value) => Value = IdentifierValidation.RequireValue(value, nameof(value));

    /// <summary>Gets the stable serialized value.</summary>
    public string Value { get; }
}

/// <summary>Stable identity of an assembly component instance, distinct from its referenced document.</summary>
public readonly record struct ComponentId
{
    /// <summary>Creates an opaque component-instance identity.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public ComponentId(string value) => Value = IdentifierValidation.RequireValue(value, nameof(value));

    /// <summary>Gets the stable serialized value.</summary>
    public string Value { get; }
}

/// <summary>Stable identity of a drawing view.</summary>
public readonly record struct ViewId
{
    /// <summary>Creates an opaque view identity.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public ViewId(string value) => Value = IdentifierValidation.RequireValue(value, nameof(value));

    /// <summary>Gets the stable serialized value.</summary>
    public string Value { get; }
}

/// <summary>Stable identity of a drawing annotation.</summary>
public readonly record struct AnnotationId
{
    /// <summary>Creates an opaque annotation identity.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public AnnotationId(string value) => Value = IdentifierValidation.RequireValue(value, nameof(value));

    /// <summary>Gets the stable serialized value.</summary>
    public string Value { get; }
}

/// <summary>Stable engineering/BOM item identity; never use a rendered row number as identity.</summary>
/// <remarks>ItemIdentity 允许 BOM 重新排序而不破坏 Balloon 或跨 Sheet 关联。</remarks>
public readonly record struct ItemIdentity
{
    /// <summary>Creates an opaque item identity.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public ItemIdentity(string value) => Value = IdentifierValidation.RequireValue(value, nameof(value));

    /// <summary>Gets the stable serialized value.</summary>
    public string Value { get; }
}

/// <summary>Stable identity of one transaction execution.</summary>
public readonly record struct TransactionId
{
    /// <summary>Creates an opaque transaction identity.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public TransactionId(string value) => Value = IdentifierValidation.RequireValue(value, nameof(value));

    /// <summary>Gets the stable serialized value.</summary>
    public string Value { get; }
}

/// <summary>Caller-provided key used to make a mutation idempotent.</summary>
public readonly record struct IdempotencyKey
{
    /// <summary>Creates an opaque idempotency key.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public IdempotencyKey(string value) => Value = IdentifierValidation.RequireValue(value, nameof(value));

    /// <summary>Gets the stable serialized value.</summary>
    public string Value { get; }
}
