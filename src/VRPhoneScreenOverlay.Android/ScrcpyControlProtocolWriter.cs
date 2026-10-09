using System.Buffers.Binary;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Android;

internal sealed class ScrcpyControlProtocolWriter(Stream stream)
{
    private const byte _injectKeycode = 0;
    private const byte _injectTouch = 2;
    private const byte _injectScroll = 3;
    private const byte _expandSettingsPanel = 6;
    private const byte _setDisplayPower = 10;
    private const byte _userActivity = 127;
    private const byte _guardedWake = 128;
    private const int _keyActionDown = 0;
    private const int _keyActionUp = 1;
    private const int _motionActionDown = 0;
    private const int _motionActionUp = 1;
    private const int _motionActionMove = 2;
    private const int _motionActionCancel = 3;
    private const int _keycodeHome = 3;
    private const int _keycodeBack = 4;
    private const int _keycodeScreenshot = 120;
    private const int _keycodeAppSwitch = 187;
    private const int _keycodeDigit0 = 7;
    private const int _keycodeEnter = 66;
    private const int _keycodeDelete = 67;
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private readonly byte[] _buffer = new byte[32];

    public async ValueTask WriteAsync(
        PhoneInputCommand command,
        CancellationToken cancellationToken)
    {
        int length = Serialize(command, _buffer);
        await _stream.WriteAsync(_buffer.AsMemory(0, length), cancellationToken)
            .ConfigureAwait(false);
    }

    internal static int Serialize(PhoneInputCommand command, Span<byte> destination)
    {
        if (destination.Length < 32)
        {
            throw new ArgumentException("The destination must contain at least 32 bytes.", nameof(destination));
        }

        return command.Kind switch
        {
            PhoneInputCommandKind.PointerDown => SerializeTouch(
                command,
                _motionActionDown,
                ushort.MaxValue,
                destination),
            PhoneInputCommandKind.PointerMove => SerializeTouch(
                command,
                _motionActionMove,
                ushort.MaxValue,
                destination),
            PhoneInputCommandKind.PointerUp => SerializeTouch(
                command,
                _motionActionUp,
                0,
                destination),
            PhoneInputCommandKind.PointerCancel => SerializeTouch(
                command,
                _motionActionCancel,
                0,
                destination),
            PhoneInputCommandKind.Back => SerializeKeyPress(_keycodeBack, destination),
            PhoneInputCommandKind.Home => SerializeKeyPress(_keycodeHome, destination),
            PhoneInputCommandKind.RecentApps => SerializeKeyPress(_keycodeAppSwitch, destination),
            PhoneInputCommandKind.OpenControlPanel => SerializeEmpty(
                _expandSettingsPanel,
                destination),
            PhoneInputCommandKind.Screenshot => SerializeKeyPress(_keycodeScreenshot, destination),
            PhoneInputCommandKind.Scroll => SerializeScroll(command, destination),
            PhoneInputCommandKind.WakeScreen => SerializeDisplayPowerOn(destination),
            PhoneInputCommandKind.RestoreDisplayPower => SerializeDisplayPowerOn(destination),
            PhoneInputCommandKind.TurnDisplayOff => SerializeDisplayPowerOff(destination),
            PhoneInputCommandKind.MaintainDisplayOff => SerializeDisplayPowerOff(destination),
            PhoneInputCommandKind.WakeScreenWhileDisplayOff => SerializeEmpty(_guardedWake, destination),
            PhoneInputCommandKind.UserActivity => SerializeEmpty(_userActivity, destination),
            PhoneInputCommandKind.UnlockDigit => SerializeUnlockDigit(command, destination),
            PhoneInputCommandKind.UnlockBackspace => SerializeKeyPress(
                _keycodeDelete,
                destination),
            PhoneInputCommandKind.UnlockConfirm => SerializeKeyPress(
                _keycodeEnter,
                destination),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command.Kind, "Unknown phone input command."),
        };
    }

    private static int SerializeUnlockDigit(
        PhoneInputCommand command,
        Span<byte> destination)
    {
        if (command.UnlockDigit is < 0 or > 9)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command),
                "Unlock digit must be between zero and nine.");
        }

        return SerializeKeyPress(_keycodeDigit0 + command.UnlockDigit, destination);
    }

    private static int SerializeDisplayPowerOn(Span<byte> destination)
    {
        destination[0] = _setDisplayPower;
        destination[1] = 1;
        return 2;
    }

    private static int SerializeDisplayPowerOff(Span<byte> destination)
    {
        destination[0] = _setDisplayPower;
        destination[1] = 0;
        return 2;
    }

    private static int SerializeScroll(PhoneInputCommand command, Span<byte> destination)
    {
        ValidatePosition(command);
        if (!float.IsFinite(command.ScrollDelta))
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Scroll delta must be finite.");
        }

        int x = MapCoordinate(command.NormalizedX, command.ScreenWidth);
        int y = MapCoordinate(command.NormalizedY, command.ScreenHeight);
        destination[0] = _injectScroll;
        BinaryPrimitives.WriteInt32BigEndian(destination[1..5], x);
        BinaryPrimitives.WriteInt32BigEndian(destination[5..9], y);
        BinaryPrimitives.WriteUInt16BigEndian(destination[9..11], checked((ushort)command.ScreenWidth));
        BinaryPrimitives.WriteUInt16BigEndian(destination[11..13], checked((ushort)command.ScreenHeight));
        BinaryPrimitives.WriteInt16BigEndian(destination[13..15], 0);
        BinaryPrimitives.WriteInt16BigEndian(destination[15..17], EncodeScroll(command.ScrollDelta));
        BinaryPrimitives.WriteInt32BigEndian(destination[17..21], 0);
        return 21;
    }

    private static int SerializeTouch(
        PhoneInputCommand command,
        int action,
        ushort pressure,
        Span<byte> destination)
    {
        ValidateTouch(command);
        int x = MapCoordinate(command.NormalizedX, command.ScreenWidth);
        int y = MapCoordinate(command.NormalizedY, command.ScreenHeight);

        destination[0] = _injectTouch;
        destination[1] = checked((byte)action);
        BinaryPrimitives.WriteInt64BigEndian(destination[2..10], command.PointerId);
        BinaryPrimitives.WriteInt32BigEndian(destination[10..14], x);
        BinaryPrimitives.WriteInt32BigEndian(destination[14..18], y);
        BinaryPrimitives.WriteUInt16BigEndian(destination[18..20], checked((ushort)command.ScreenWidth));
        BinaryPrimitives.WriteUInt16BigEndian(destination[20..22], checked((ushort)command.ScreenHeight));
        BinaryPrimitives.WriteUInt16BigEndian(destination[22..24], pressure);
        BinaryPrimitives.WriteInt32BigEndian(destination[24..28], 0);
        BinaryPrimitives.WriteInt32BigEndian(destination[28..32], 0);
        return 32;
    }

    private static int SerializeKeyPress(int keycode, Span<byte> destination)
    {
        SerializeKeyEvent(_keyActionDown, keycode, destination);
        SerializeKeyEvent(_keyActionUp, keycode, destination[14..]);
        return 28;
    }

    private static void SerializeKeyEvent(int action, int keycode, Span<byte> destination)
    {
        destination[0] = _injectKeycode;
        destination[1] = checked((byte)action);
        BinaryPrimitives.WriteInt32BigEndian(destination[2..6], keycode);
        BinaryPrimitives.WriteInt32BigEndian(destination[6..10], 0);
        BinaryPrimitives.WriteInt32BigEndian(destination[10..14], 0);
    }

    private static int SerializeEmpty(byte messageType, Span<byte> destination)
    {
        destination[0] = messageType;
        return 1;
    }

    private static void ValidateTouch(PhoneInputCommand command)
    {
        ValidatePosition(command);
    }

    private static void ValidatePosition(PhoneInputCommand command)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(command.ScreenWidth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(command.ScreenWidth, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(command.ScreenHeight, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(command.ScreenHeight, ushort.MaxValue);
        if (!float.IsFinite(command.NormalizedX) || !float.IsFinite(command.NormalizedY))
        {
            throw new ArgumentOutOfRangeException(nameof(command), "Pointer coordinates must be finite.");
        }
    }

    private static int MapCoordinate(float normalized, int dimension) =>
        (int)MathF.Round(Math.Clamp(normalized, 0, 1) * (dimension - 1));

    private static short EncodeScroll(float value)
    {
        float clamped = Math.Clamp(value, -16f, 16f);
        if (clamped <= -16f)
        {
            return short.MinValue;
        }

        return checked((short)MathF.Round(clamped / 16f * short.MaxValue));
    }
}
