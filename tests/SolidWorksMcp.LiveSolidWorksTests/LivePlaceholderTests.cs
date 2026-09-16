namespace SolidWorksMcp.LiveSolidWorksTests;

public sealed class LivePlaceholderTests
{
    // Real COM evidence is opt-in and cannot be represented by this placeholder as a passed test.
    // 真实 COM 证据必须显式 opt-in；这个占位测试不能冒充 Live 能力已通过。
    // This is deliberately skipped until Issue #51 supplies a real isolated COM harness.
    // 在 Issue #51 提供真实的隔离 COM harness 前，这里必须明确跳过。
    [Fact(Skip = "Live SOLIDWORKS harness is not implemented; tracked by Issue #51.")]
    public void LiveSuiteRequiresExplicitLocalOptIn()
    {
        Assert.True(true);
    }
}
