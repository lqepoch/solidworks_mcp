using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.Fake;

/// <summary>Deterministic selector resolver used to prove the vendor-neutral selection contract.</summary>
/// <remarks>
/// FakeCad uses a documented test-only token format and the same precedence as the native design: persistent token,
/// geometry signature, then semantic name.  It never exposes a list index, and an expected state hash becomes stale
/// after any document mutation.  FakeCad 用测试专用 token 验证与原生设计相同的优先级；绝不暴露列表索引，文档 mutation
/// 后 ExpectedStateHash 必须失效。
/// </remarks>
internal sealed class FakeCadSelectionService(FakeCadSession session) : ICadSelectionService
{
    private readonly FakeCadSession session = session;

    /// <inheritdoc />
    public Task<OperationResult<CadSelectionSnapshot>> ResolveAsync(
        CadEntitySelector selector,
        CancellationToken cancellationToken = default)
    {
        const string operation = "selection.resolve";
        if (selector is null)
        {
            return Task.FromResult(FakeCadResults.Invalid<CadSelectionSnapshot>(operation, "The declarative entity selector is required."));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(FakeCadResults.Cancelled<CadSelectionSnapshot>(operation));
        }

        if (session.IsClosed)
        {
            return Task.FromResult(FakeCadResults.Closed<CadSelectionSnapshot>(operation));
        }

        if (!session.Supports(CadCapabilityNames.Selection, out CadCapability capability))
        {
            return Task.FromResult(FakeCadResults.Unsupported<CadSelectionSnapshot>(operation, capability));
        }

        if (!selector.HasResolutionHint)
        {
            return Task.FromResult(FakeCadResults.Invalid<CadSelectionSnapshot>(
                operation,
                "A selector must contain a persistent reference, geometry signature or semantic name."));
        }

        if (!session.TryGetDocument(selector.DocumentId, out FakeCadDocument? document) || document is null)
        {
            return Task.FromResult(FakeCadResults.NotFound<CadSelectionSnapshot>(operation, selector.DocumentId.Value));
        }

        string stateHash = document.StateHash;
        if (!string.IsNullOrWhiteSpace(selector.ExpectedStateHash)
            && !selector.ExpectedStateHash.Equals(stateHash, StringComparison.Ordinal))
        {
            return Task.FromResult(Stale(operation, selector.ExpectedStateHash!, stateHash));
        }

        CadInspectionSnapshot inspection = document.BuildInspection();
        (string Identity, CadSelectionResolution Resolution)? resolved = ResolveFromInspection(selector, inspection);
        if (resolved is null)
        {
            return Task.FromResult(FakeCadResults.NotFound<CadSelectionSnapshot>(
                operation,
                $"{selector.EntityKind}:{selector.SemanticName ?? selector.PersistentReference?.Token ?? selector.GeometrySignature?.Value ?? "unresolved"}"));
        }

        CadSelectionSnapshot snapshot = new()
        {
            DocumentId = selector.DocumentId,
            Entity = new CadEntityReference { EntityKind = selector.EntityKind, Identity = resolved.Value.Identity },
            Resolution = resolved.Value.Resolution,
            StateHash = stateHash,
            SelectorFingerprint = Fingerprint(selector),
            PersistentReference = new CadPersistentReference
            {
                Format = "fake.persist1",
                Token = $"fake|{selector.DocumentId.Value}|{selector.EntityKind}|{resolved.Value.Identity}",
            },
        };
        return Task.FromResult(
            FakeCadResults.Success(
                snapshot,
                operation,
                new EvidenceObservation("document.id", selector.DocumentId.Value),
                new EvidenceObservation("selection.entity-kind", selector.EntityKind.ToString()),
                new EvidenceObservation("selection.entity-id", resolved.Value.Identity),
                new EvidenceObservation("selection.resolution", resolved.Value.Resolution.ToString()),
                new EvidenceObservation("selection.selector-fingerprint", snapshot.SelectorFingerprint),
                new EvidenceObservation("state.hash", stateHash)));
    }

    private static (string Identity, CadSelectionResolution Resolution)? ResolveFromInspection(
        CadEntitySelector selector,
        CadInspectionSnapshot inspection)
    {
        if (selector.PersistentReference is not null
            && TryParseFakeReference(selector.PersistentReference, selector, out string? persistentIdentity)
            && ExistsInInspection(selector, inspection, persistentIdentity!))
        {
            return (persistentIdentity!, CadSelectionResolution.PersistentReference);
        }

        if (selector.GeometrySignature is not null
            && TryParseFakeReferenceValue(selector.GeometrySignature.Value, selector, out string? geometryIdentity)
            && ExistsInInspection(selector, inspection, geometryIdentity!))
        {
            return (geometryIdentity!, CadSelectionResolution.GeometrySignature);
        }

        if (string.IsNullOrWhiteSpace(selector.SemanticName))
        {
            return null;
        }

        string name = selector.SemanticName.Trim();
        string? identity = selector.EntityKind switch
        {
            CadEntityKind.Body => inspection.Bodies
                .Where(body => body.Name.Equals(name, StringComparison.Ordinal) || body.BodyId.Value.Equals(name, StringComparison.Ordinal))
                .Select(body => body.BodyId.Value)
                .SingleOrDefault(),
            CadEntityKind.Feature => inspection.Features
                .Where(feature => feature.Name.Equals(name, StringComparison.Ordinal) || feature.FeatureId.Value.Equals(name, StringComparison.Ordinal))
                .Select(feature => feature.FeatureId.Value)
                .SingleOrDefault(),
            CadEntityKind.Component => inspection.Components
                .Where(component => component.ComponentId.Value.Equals(name, StringComparison.Ordinal))
                .Select(component => component.ComponentId.Value)
                .SingleOrDefault(),
            CadEntityKind.DrawingView => inspection.Views
                .Where(view => view.Name.Equals(name, StringComparison.Ordinal) || view.ViewId.Value.Equals(name, StringComparison.Ordinal))
                .Select(view => view.ViewId.Value)
                .SingleOrDefault(),
            CadEntityKind.Annotation => inspection.Annotations
                .Where(annotation => annotation.AnnotationId.Value.Equals(name, StringComparison.Ordinal))
                .Select(annotation => annotation.AnnotationId.Value)
                .SingleOrDefault(),
            _ => null,
        };

        return identity is null ? null : (identity, CadSelectionResolution.SemanticSelector);
    }

    private static bool ExistsInInspection(CadEntitySelector selector, CadInspectionSnapshot inspection, string identity)
    {
        return selector.EntityKind switch
        {
            CadEntityKind.Body => inspection.Bodies.Any(body => body.BodyId.Value.Equals(identity, StringComparison.Ordinal)),
            CadEntityKind.Feature => inspection.Features.Any(feature => feature.FeatureId.Value.Equals(identity, StringComparison.Ordinal)),
            CadEntityKind.Component => inspection.Components.Any(component => component.ComponentId.Value.Equals(identity, StringComparison.Ordinal)),
            CadEntityKind.DrawingView => inspection.Views.Any(view => view.ViewId.Value.Equals(identity, StringComparison.Ordinal)),
            CadEntityKind.Annotation => inspection.Annotations.Any(annotation => annotation.AnnotationId.Value.Equals(identity, StringComparison.Ordinal)),
            // The current FakeCad snapshots do not contain topology/sketch entities yet, so those kinds cannot be
            // resolved by an invented token.  Native B03 will provide the document-specific topology registry.
            // 当前 FakeCad snapshot 尚未包含 topology/sketch entity，不能接受伪造 token；B03 原生 registry 再实现。
            _ => false,
        };
    }

    private static bool TryParseFakeReference(
        CadPersistentReference reference,
        CadEntitySelector selector,
        out string? identity)
    {
        identity = null;
        if (!reference.Format.Equals("fake.persist1", StringComparison.Ordinal))
        {
            return false;
        }

        return TryParseFakeReferenceValue(reference.Token, selector, out identity);
    }

    private static bool TryParseFakeReferenceValue(
        string value,
        CadEntitySelector selector,
        out string? identity)
    {
        identity = null;
        string[] fields = value.Split('|');
        if (fields.Length != 4
            || !fields[0].Equals("fake", StringComparison.Ordinal)
            || !fields[1].Equals(selector.DocumentId.Value, StringComparison.Ordinal)
            || !fields[2].Equals(selector.EntityKind.ToString(), StringComparison.Ordinal))
        {
            return false;
        }

        identity = string.IsNullOrWhiteSpace(fields[3]) ? null : fields[3];
        return identity is not null;
    }

    private static OperationResult<CadSelectionSnapshot> Stale(string operation, string expected, string actual) => FakeCadResults.Failure<CadSelectionSnapshot>(
        operation,
        new OperationError(
            ErrorCodes.SelectionStale,
            "The selector was captured against an older CAD document state.",
            ErrorCategories.State,
            remediation: "Inspect the document again and create a fresh declarative selector.",
            details: new Dictionary<string, string>
            {
                ["expected-state-hash"] = expected,
                ["actual-state-hash"] = actual,
            }));

    private static string Fingerprint(CadEntitySelector selector)
    {
        string canonical = JsonSerializer.Serialize(selector);
        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()}";
    }
}
