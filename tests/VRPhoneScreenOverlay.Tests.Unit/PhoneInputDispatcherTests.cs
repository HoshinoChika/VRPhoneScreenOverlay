using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Input;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class PhoneInputDispatcherTests
{
    [Fact]
    public async Task KeepsLatestMoveBeforePointerUp()
    {
        List<PhoneInputCommand> sent = [];
        TaskCompletionSource downEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseDown = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using PhoneInputDispatcher dispatcher = new(async (command, cancellationToken) =>
        {
            if (command.Kind == PhoneInputCommandKind.PointerDown)
            {
                downEntered.TrySetResult();
                await releaseDown.Task.WaitAsync(cancellationToken);
            }

            lock (sent)
            {
                sent.Add(command);
            }
        });

        ValueTask down = dispatcher.EnqueueAsync(Pointer(1, PhoneInputCommandKind.PointerDown, 0.1f), CancellationToken.None);
        await downEntered.Task;
        await dispatcher.EnqueueAsync(Pointer(2, PhoneInputCommandKind.PointerMove, 0.2f), CancellationToken.None);
        await dispatcher.EnqueueAsync(Pointer(3, PhoneInputCommandKind.PointerMove, 0.3f), CancellationToken.None);
        ValueTask up = dispatcher.EnqueueAsync(Pointer(4, PhoneInputCommandKind.PointerUp, 0.4f), CancellationToken.None);
        releaseDown.TrySetResult();

        await down;
        await up;

        Assert.Collection(
            sent,
            command => Assert.Equal(PhoneInputCommandKind.PointerDown, command.Kind),
            command =>
            {
                Assert.Equal(PhoneInputCommandKind.PointerMove, command.Kind);
                Assert.Equal(0.3f, command.NormalizedX);
            },
            command => Assert.Equal(PhoneInputCommandKind.PointerUp, command.Kind));
        Assert.Equal(1, dispatcher.Snapshot.ReplacedPointerMoves);
    }

    [Fact]
    public async Task SendsCancelWhenDisposedWithActivePointer()
    {
        List<PhoneInputCommandKind> sent = [];
        PhoneInputDispatcher dispatcher = new((command, _) =>
        {
            sent.Add(command.Kind);
            return ValueTask.CompletedTask;
        });

        await dispatcher.EnqueueAsync(
            Pointer(1, PhoneInputCommandKind.PointerDown, 0.5f),
            CancellationToken.None);
        await dispatcher.DisposeAsync();

        Assert.Equal(
            [PhoneInputCommandKind.PointerDown, PhoneInputCommandKind.PointerCancel],
            sent);
    }

    private static PhoneInputCommand Pointer(
        long sequence,
        PhoneInputCommandKind kind,
        float x) => new(
        sequence,
        kind,
        -2,
        x,
        0.5f,
        1080,
        2400,
        DateTimeOffset.UtcNow);
}
