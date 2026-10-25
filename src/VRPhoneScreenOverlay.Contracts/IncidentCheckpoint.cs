namespace VRPhoneScreenOverlay.Contracts;

/// <summary>Low frequency lifecycle boundaries, never a per-frame event stream.</summary>
public sealed record IncidentCheckpoint(
    string Stage,
    string Phase,
    string? Backend = null,
    int Width = 0,
    int Height = 0,
    int Stride = 0,
    int Bytes = 0,
    int Generation = 0)
{
    public string? AttemptId { get; init; }
    public double? AttemptElapsedMilliseconds { get; init; }
    public VideoGeometryDiagnostic? Geometry { get; init; }
}

public interface IIncidentRecorder
{
    public ValueTask CheckpointAsync(IncidentCheckpoint checkpoint, CancellationToken cancellationToken);
    public ValueTask FailureAsync(Exception exception, string kind, string? attemptId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
