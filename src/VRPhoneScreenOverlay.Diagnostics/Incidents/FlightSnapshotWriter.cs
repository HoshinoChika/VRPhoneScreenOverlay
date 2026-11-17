namespace VRPhoneScreenOverlay.Diagnostics;

// One last-record reference per capture session; no queue. The provider publishes
// immutable records. Retry after a failed write; force a durable copy for faults.
internal sealed class FlightSnapshotWriter(string path)
{
    private FlightRecord? _lastSaved;
    internal async ValueTask<bool> WriteAsync(FlightRecord record, ReadOnlyMemory<byte>? json,
        bool durable, CancellationToken token)
    {
        if (!durable && ReferenceEquals(record, _lastSaved)) { return false; }
        await IncidentStorage.WriteBytesAsync(path,
            json ?? IncidentStorage.Serialize(record, FlightJsonContext.Default.FlightRecord), token, durable).ConfigureAwait(false);
        _lastSaved = record;
        return true;
    }
}
