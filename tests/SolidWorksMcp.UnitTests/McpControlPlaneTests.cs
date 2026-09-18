using System.Collections.Immutable;
using SolidWorksMcp.CadAbstractions;
using SolidWorksMcp.Core;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.UnitTests;

/// <summary>
/// Proves the MCP control plane independently from SOLIDWORKS COM.
/// 在不启动 SOLIDWORKS COM 的前提下证明 MCP control plane 的边界。
/// </summary>
/// <remarks>
/// These tests intentionally exercise admission and audit, not CAD geometry. Geometry belongs behind the provider
/// contract; the MCP product must remain safe and deterministic even when no provider is installed. 这些测试刻意只
/// 验证准入和审计，不验证 CAD 几何；几何属于 Provider contract，即使没有安装 Provider，MCP 产品也必须安全且确定。
/// </remarks>
public sealed class McpControlPlaneTests
{
    [Fact]
    public void DocumentMutationRequiresTheExactBoundSessionAndState()
    {
        var controlPlane = CreateControlPlane(
            new McpOperationDescriptor
            {
                Name = "drawing.repair",
                Tier = "mutation",
                RiskLevel = CadRiskLevel.DrawingMutation,
                MutatesCad = true,
                RequiresSession = true,
                RequiresIdempotency = true,
                RequiresExpectedStateHash = true,
                RequiredCapability = CadCapabilityNames.DrawingMutation,
            });

        OperationError? unbound = controlPlane.Validate(new McpInvocationContext
        {
            OperationId = "mcp:test:unbound",
            ToolName = "drawing.repair",
            IdempotencyKey = "retry-1",
        });
        Assert.Equal(ErrorCodes.StateConflict, unbound?.Code);

        OperationError? stale = controlPlane.Validate(new McpInvocationContext
        {
            OperationId = "mcp:test:stale",
            ToolName = "drawing.repair",
            IdempotencyKey = "retry-1",
            SessionId = new SessionId("session-1"),
            DocumentId = new DocumentId("drawing-1"),
        });
        Assert.Equal(ErrorCodes.StateConflict, stale?.Code);

        OperationError? accepted = controlPlane.Validate(new McpInvocationContext
        {
            OperationId = "mcp:test:accepted",
            ToolName = "drawing.repair",
            IdempotencyKey = "retry-1",
            SessionId = new SessionId("session-1"),
            DocumentId = new DocumentId("drawing-1"),
            ExpectedStateHash = "sha256:before",
        });
        Assert.Null(accepted);
    }

    [Fact]
    public async Task UnknownOperationIsRejectedBeforeHandlerAndAudited()
    {
        var sink = new InMemoryMcpAuditSink();
        var controlPlane = CreateControlPlane(sink);
        bool called = false;

        OperationResult<string> result = await controlPlane.ExecuteAsync(
            new McpInvocationContext
            {
                OperationId = "mcp:test:unknown",
                ToolName = "com.execute-anything",
            },
            _ =>
            {
                called = true;
                return Task.FromResult(OperationResults.Success(
                    "should-not-run",
                    "mcp:test:unknown",
                    new OperationEvidence("test")));
            });

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.InvalidRequest, result.Error!.Code);
        Assert.False(called);
        Assert.Contains(sink.Records, record =>
            record.Event == "admission.rejected"
            && record.ErrorCode == ErrorCodes.InvalidRequest);
    }

    [Fact]
    public async Task AcceptedExecutionAuditsOutcomeWithoutLeakingTargetPath()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "solidworks-mcp-control-plane", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            string target = Path.Combine(workspace, "part.sldprt");
            var sink = new InMemoryMcpAuditSink();
            var controlPlane = CreateControlPlane(
                sink,
                new McpOperationDescriptor
                {
                    Name = "cad.create-part",
                    Tier = "mutation",
                    RiskLevel = CadRiskLevel.ModelMutation,
                    MutatesCad = true,
                    RequiresSession = true,
                    RequiresIdempotency = true,
                    RequiresExpectedStateHash = false,
                    RequiredCapability = CadCapabilityNames.PartMutation,
                },
                new CadPathAllowlist([workspace]));

            OperationResult<string> result = await controlPlane.ExecuteAsync(
                new McpInvocationContext
                {
                    OperationId = "mcp:test:create",
                    ToolName = "cad.create-part",
                    IdempotencyKey = "create-1",
                    SessionId = new SessionId("session-1"),
                    TargetPath = target,
                    RequiredPathExtension = ".sldprt",
                },
                _ => Task.FromResult(OperationResults.Success(
                    "verified",
                    "mcp:test:create",
                    new OperationEvidence("fake-cad"))));

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Contains(sink.Records, record => record.Event == "admission.accepted");
            Assert.Contains(sink.Records, record => record.Event == "execution.completed");
            Assert.All(sink.Records, record => Assert.DoesNotContain(target, record.ToString(), StringComparison.Ordinal));
            Assert.Contains(sink.Records, record => record.TargetExtension == ".sldprt");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task OutsidePathIsRejectedBeforeProviderHandler()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "solidworks-mcp-control-plane", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var controlPlane = CreateControlPlane(
                new McpOperationDescriptor
                {
                    Name = "cad.create-part",
                    Tier = "mutation",
                    RiskLevel = CadRiskLevel.ModelMutation,
                    MutatesCad = true,
                    RequiresSession = true,
                    RequiresIdempotency = true,
                    RequiresExpectedStateHash = false,
                    RequiredCapability = CadCapabilityNames.PartMutation,
                },
                pathAllowlist: new CadPathAllowlist([workspace]));

            OperationResult<string> result = await controlPlane.ExecuteAsync(
                new McpInvocationContext
                {
                    OperationId = "mcp:test:outside",
                    ToolName = "cad.create-part",
                    IdempotencyKey = "outside-1",
                    SessionId = new SessionId("session-1"),
                    TargetPath = Path.Combine(Path.GetTempPath(), "not-allowlisted.sldprt"),
                    RequiredPathExtension = ".sldprt",
                },
                _ => Task.FromResult(OperationResults.Success(
                    "should-not-run",
                    "mcp:test:outside",
                    new OperationEvidence("test"))));

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCodes.PathNotAllowed, result.Error!.Code);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static McpControlPlane CreateControlPlane(
        params McpOperationDescriptor[] descriptors) =>
        CreateControlPlane(new InMemoryMcpAuditSink(), descriptors, CadPathAllowlist.DenyAll);

    private static McpControlPlane CreateControlPlane(
        InMemoryMcpAuditSink sink,
        McpOperationDescriptor descriptor,
        CadPathAllowlist pathAllowlist) =>
        CreateControlPlane(sink, [descriptor], pathAllowlist);

    private static McpControlPlane CreateControlPlane(
        McpOperationDescriptor descriptor,
        CadPathAllowlist pathAllowlist) =>
        CreateControlPlane(new InMemoryMcpAuditSink(), [descriptor], pathAllowlist);

    private static McpControlPlane CreateControlPlane(
        InMemoryMcpAuditSink sink,
        IEnumerable<McpOperationDescriptor>? descriptors = null,
        CadPathAllowlist? pathAllowlist = null) =>
        new(
            new TestCatalog(descriptors ?? []),
            new CadCapabilitySet(
            [
                new CadCapability(CadCapabilityNames.PartMutation, supported: true),
                new CadCapability(CadCapabilityNames.DrawingMutation, supported: true),
            ]),
            new SolidWorksMcpConfiguration(providerMode: ProviderModes.Fake),
            pathAllowlist ?? CadPathAllowlist.DenyAll,
            sink);

    private sealed class TestCatalog(IEnumerable<McpOperationDescriptor> descriptors) : IMcpOperationCatalog
    {
        private readonly ImmutableArray<McpOperationDescriptor> operations = [.. descriptors];

        public bool TryGet(string name, out McpOperationDescriptor? descriptor)
        {
            descriptor = operations.FirstOrDefault(operation =>
                string.Equals(operation.Name, name, StringComparison.Ordinal));
            return descriptor is not null;
        }

        public ImmutableArray<McpOperationDescriptor> List() => operations;
    }
}
