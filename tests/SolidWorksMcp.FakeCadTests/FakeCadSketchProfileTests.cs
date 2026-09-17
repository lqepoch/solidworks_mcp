using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Protocol;
using SolidWorksMcp.Provider.Fake;

namespace SolidWorksMcp.FakeCadTests;

/// <summary>
/// Verifies the FakeCad contract for the structured initial sketch profile.
/// 验证结构化初始草图 profile 的 FakeCad 契约。
/// </summary>
public sealed class FakeCadSketchProfileTests
{
    /// <summary>
    /// Invalid geometry is rejected before a document is registered, with stable validation evidence.
    /// 无效几何必须在 document 注册前被拒绝，并返回稳定的验证证据。
    /// </summary>
    [Fact]
    public async Task InvalidInitialSketchProfileFailsClosedBeforeDocumentRegistration()
    {
        await using var provider = new FakeCadProvider();
        await using ICadSession session = (await provider.StartSessionAsync(new CadSessionOptions())).RequireSuccess();
        var requestedDocumentId = new DocumentId("profile-fail-closed");

        OperationResult<ICadPartDocument> rejected = await session.CreatePartAsync(new CreatePartRequest
        {
            RequestedDocumentId = requestedDocumentId,
            InitialSketchProfile = DisconnectedProfile(),
        });

        Assert.False(rejected.IsSuccess);
        Assert.Equal(ErrorCodes.InvalidRequest, rejected.Error!.Code);
        Assert.Contains(
            rejected.Evidence!.Observations,
            observation => observation.Key == "initial-sketch-profile.status" && observation.Value == "rejected");
        Assert.Contains(
            rejected.Evidence.Observations,
            observation => observation.Key == "initial-sketch-profile.validation"
                && observation.Value == "segment-0-is-disconnected");

        // Reusing the same identity proves the rejected request did not leave a half-created document behind.
        // 使用同一 identity 再次创建，证明 rejected request 没有留下半创建 document。
        ICadPartDocument accepted = (await session.CreatePartAsync(new CreatePartRequest
        {
            RequestedDocumentId = requestedDocumentId,
            InitialSketchProfile = ValidDProfile(),
        })).RequireSuccess();

        Assert.Equal(requestedDocumentId, accepted.DocumentId);
    }

    /// <summary>
    /// A valid profile produces deterministic create/inspect evidence and participates in the state hash.
    /// 有效 profile 必须产生确定性的 create/inspect 证据，并参与 state hash。
    /// </summary>
    [Fact]
    public async Task ValidInitialSketchProfileIsObservableAndDeterministic()
    {
        SketchProfileRequest profile = ValidDProfile();
        string firstHash;
        string firstStateHash;
        await using (var provider = new FakeCadProvider())
        {
            await using ICadSession session = (await provider.StartSessionAsync(new CadSessionOptions())).RequireSuccess();
            OperationResult<ICadPartDocument> created = await session.CreatePartAsync(new CreatePartRequest
            {
                RequestedDocumentId = new DocumentId("profile-evidence"),
                InitialSketchProfile = profile,
            });
            ICadPartDocument part = created.RequireSuccess();

            firstHash = created.Evidence!.Observations
                .Single(observation => observation.Key == "initial-sketch-profile.geometry-hash")
                .Value;
            firstStateHash = part.StateHash;
            Assert.Equal("accepted", Observation(created, "initial-sketch-profile.status"));
            Assert.Equal("connected-closed-loop", Observation(created, "initial-sketch-profile.validation"));
            Assert.Equal("4", Observation(created, "initial-sketch-profile.segment-count"));
            Assert.Equal("3", Observation(created, "initial-sketch-profile.line-count"));
            Assert.Equal("1", Observation(created, "initial-sketch-profile.three-point-arc-count"));

            OperationResult<CadInspectionSnapshot> inspected = await session.Inspection.InspectAsync(part.DocumentId);
            CadInspectionSnapshot inspection = inspected.RequireSuccess();
            Assert.Equal(firstStateHash, inspection.Document.StateHash);
            Assert.Equal(firstHash, Observation(inspected, "initial-sketch-profile.geometry-hash"));
            Assert.Equal("accepted", Observation(inspected, "initial-sketch-profile.status"));
        }

        // The same vendor-neutral input must yield the same digest in another process-free fake session.
        // 相同的厂商无关输入在另一个无进程 Fake session 中必须得到相同 digest。
        await using (var provider = new FakeCadProvider())
        {
            await using ICadSession session = (await provider.StartSessionAsync(new CadSessionOptions())).RequireSuccess();
            OperationResult<ICadPartDocument> created = await session.CreatePartAsync(new CreatePartRequest
            {
                RequestedDocumentId = new DocumentId("profile-evidence"),
                InitialSketchProfile = profile,
            });

            Assert.Equal(firstHash, Observation(created, "initial-sketch-profile.geometry-hash"));
            Assert.Equal(firstStateHash, created.Value!.StateHash);
        }

        // A profile-bearing document must not have the same state material as an otherwise identical empty request.
        // 携带 profile 的 document 不应与其他条件相同但没有 profile 的请求拥有相同 state material。
        await using (var provider = new FakeCadProvider())
        {
            await using ICadSession session = (await provider.StartSessionAsync(new CadSessionOptions())).RequireSuccess();
            ICadPartDocument empty = (await session.CreatePartAsync(new CreatePartRequest
            {
                RequestedDocumentId = new DocumentId("profile-evidence"),
            })).RequireSuccess();

            Assert.NotEqual(firstStateHash, empty.StateHash);
        }
    }

    private static string Observation<T>(OperationResult<T> result, string key) =>
        result.Evidence!.Observations.Single(observation => observation.Key == key).Value;

    private static SketchProfileRequest DisconnectedProfile() => new()
    {
        Segments =
        [
            Curve(SketchCurveKind.Line, Point(0d, 0d), Point(10d, 0d)),
            Curve(SketchCurveKind.Line, Point(11d, 0d), Point(10d, 10d)),
            Curve(SketchCurveKind.Line, Point(10d, 10d), Point(0d, 0d)),
        ],
    };

    private static SketchProfileRequest ValidDProfile() => new()
    {
        Segments =
        [
            Curve(SketchCurveKind.Line, Point(-20d, -15d), Point(0d, -15d)),
            Curve(SketchCurveKind.ThreePointArc, Point(0d, -15d), Point(0d, 15d), Point(15d, 0d)),
            Curve(SketchCurveKind.Line, Point(0d, 15d), Point(-20d, 15d)),
            Curve(SketchCurveKind.Line, Point(-20d, 15d), Point(-20d, -15d)),
        ],
    };

    private static SketchCurveRequest Curve(
        SketchCurveKind kind,
        Coordinate2D start,
        Coordinate2D end,
        Coordinate2D? through = null) => new()
        {
            Kind = kind,
            Start = start,
            Through = through ?? start,
            End = end,
        };

    private static Coordinate2D Point(double x, double y) => new(
        Length.FromMillimeters(x),
        Length.FromMillimeters(y));
}
