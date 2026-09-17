using Xunit;

// A SOLIDWORKS process is a single-writer STA resource.  xUnit normally parallelizes test classes inside one
// assembly, which would let B03/B04 attach and mutate the same explicit PID concurrently.  Disable only this Live
// assembly; Hosted-safe Unit/Contract/Fake tests keep their normal parallel scheduling.
// SOLIDWORKS 进程是 single-writer STA 资源。xUnit 默认会并行运行同一程序集中的测试类，可能让 B03/B04 同时
// attach 并 mutation 同一个明确 PID。这里只禁用 Live 程序集并行，Hosted-safe 的 Unit/Contract/Fake 仍保持默认并行。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
