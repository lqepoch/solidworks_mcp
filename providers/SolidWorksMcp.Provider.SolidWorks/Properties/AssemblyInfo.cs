using System.Runtime.CompilerServices;

// The internal test friend exposes only dispatcher/session guards, never the vendor COM interfaces to product APIs.
// 此 friend 只用于测试 dispatcher/session guard，不会把厂商 COM 接口暴露给产品 API 或 MCP 调用方。
[assembly: InternalsVisibleTo("SolidWorksMcp.LiveSolidWorksTests")]
