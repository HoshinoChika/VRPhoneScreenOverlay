using Vortice.MediaFoundation;

namespace VRPhoneScreenOverlay.Media;

internal readonly record struct MftEvent(MediaEventTypes Type, uint InputStream = 0);

/// <summary>One blocking native event reader per decoder; no per-event tasks or managed event backlog.</summary>
internal sealed class MftEventPump
{
    private readonly Thread _thread;
    private volatile bool _stopping;
    public bool IsCurrentThread => Thread.CurrentThread == _thread;
    public bool IsAlive => _thread.IsAlive;

    public MftEventPump(Func<MftEvent> read, MftAsyncFlow flow, object nativeGate,
        Action output, Action available, Action<Exception> failure, Action finished)
    {
        _thread = new Thread(() =>
        {
            try
            {
                while (!_stopping)
                {
                    MftEvent notification = read();
                    bool ready = false;
                    lock (nativeGate)
                    {
                        if (_stopping) { break; }
                        switch (notification.Type)
                        {
                            case MediaEventTypes.TransformNeedInput: flow.GrantInput(notification.InputStream); break;
                            case MediaEventTypes.TransformHaveOutput: output(); ready = true; break;
                            case MediaEventTypes.TransformDrainComplete: flow.CompleteDrain(); break;
                            case MediaEventTypes.Error:
                                throw new MediaDecoderException("MEDIA_MFT_EVENT_ERROR", "解码器报告了异步错误事件");
                        }
                    }
                    if (ready) { available(); }
                }
            }
            catch (Exception error) { if (!_stopping) { failure(error); } }
            finally { finished(); }
        })
        { IsBackground = true, Name = "VRPSO MFT events" };
        _thread.Start();
    }

    internal static MftEvent Parse(IMFMediaEvent notification)
    {
        notification.Status.CheckError();
        uint stream = 0;
        if (notification.EventType == MediaEventTypes.TransformNeedInput)
        { notification.GetUInt32(EventAttributeKeys.TransformInputStreamId, out stream).CheckError(); }
        return new(notification.EventType, stream);
    }

    public void RequestStop() => _stopping = true;
    // Caller shuts down the native event source to release its blocking read first.
    public bool Join(TimeSpan timeout) => !_thread.IsAlive || _thread.Join(timeout);
}
