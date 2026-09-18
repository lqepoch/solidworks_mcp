using System.Collections.Concurrent;
using SolidWorksMcp.Protocol;

namespace SolidWorksMcp.Provider.Fake;

/// <summary>Stable failure points used by contract tests to exercise provider error paths.</summary>
public static class FakeCadFailurePoints
{
    /// <summary>Failure while starting the fake session.</summary>
    public const string StartSession = "session.start";

    /// <summary>Failure while creating any document kind.</summary>
    public const string CreateDocument = "document.create";

    /// <summary>Failure while creating a part body.</summary>
    public const string CreateBody = "part.create-body";

    /// <summary>Failure while creating an extrusion feature.</summary>
    public const string AddExtrusion = "part.add-extrusion";

    /// <summary>Failure while changing a named model dimension.</summary>
    public const string SetDimension = "part.set-dimension";

    /// <summary>Failure while inserting an assembly component.</summary>
    public const string InsertComponent = "assembly.insert-component";

    /// <summary>Failure while adding an assembly mate.</summary>
    public const string AddMate = "assembly.add-mate";

    /// <summary>Failure while adding a drawing view.</summary>
    public const string AddView = "drawing.add-view";

    /// <summary>Failure while adding a drawing annotation.</summary>
    public const string AddAnnotation = "drawing.add-annotation";

    /// <summary>Failure while adding a native surface-finish symbol.</summary>
    public const string AddSurfaceFinishSymbol = "drawing.add-surface-finish-symbol";

    /// <summary>Failure while adding native center marks.</summary>
    public const string AddCenterMarks = "drawing.add-center-marks";

    /// <summary>Failure while rebuilding a document.</summary>
    public const string Rebuild = "document.rebuild";

    /// <summary>Failure while saving a document.</summary>
    public const string Save = "document.save";

    /// <summary>Failure while inspecting a document.</summary>
    public const string Inspect = "document.inspect";

    /// <summary>Failure while exporting a document.</summary>
    public const string Export = "document.export";

    /// <summary>Failure while closing a session.</summary>
    public const string CloseSession = "session.close";
}

/// <summary>Thread-safe one-shot failure queue for deterministic provider contract tests.</summary>
/// <remarks>
/// Each enqueued error is consumed once, so tests can prove recovery after a transient failure without timing races.
/// 每个错误只消费一次，测试可以验证一次性故障后的恢复，不依赖 sleep 或线程时序。
/// </remarks>
public sealed class FakeCadFailureInjector
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<OperationError>> failures = new(StringComparer.Ordinal);

    /// <summary>Queues one failure for a named operation point.</summary>
    public void Enqueue(string failurePoint, OperationError error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failurePoint);
        ArgumentNullException.ThrowIfNull(error);
        failures.GetOrAdd(failurePoint.Trim(), static _ => new ConcurrentQueue<OperationError>()).Enqueue(error);
    }

    /// <summary>Consumes one queued failure, if present.</summary>
    public bool TryTake(string failurePoint, out OperationError? error)
    {
        error = null;
        return failures.TryGetValue(failurePoint, out ConcurrentQueue<OperationError>? queue)
            && queue.TryDequeue(out error);
    }
}
