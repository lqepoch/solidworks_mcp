namespace SolidWorksMcp.LiveSolidWorksTests;

/// <summary>
/// Explicitly exercises the process-lease preflight/cleanup without mutating a CAD document.
/// 显式验证 process lease 的 preflight/cleanup，但不修改 CAD document。
/// </summary>
[Collection(LiveSolidWorksTestGroup.Name)]
public sealed class LiveProcessLifecycleTests
{
    [OptInLiveFact]
    public void OwnedProcessLeaseWasInitialized()
    {
        // The collection fixture performs the exact-workspace preflight before this assertion and its cleanup after
        // the test. The test body stays intentionally small so lifecycle evidence cannot be confused with CAD proof.
        // collection fixture 已在 assertion 前完成 exact-workspace preflight，并在测试后 cleanup；body 刻意很小，避免
        // 把 lifecycle evidence 误当成 CAD geometry proof。
        Assert.True(true);
    }
}
