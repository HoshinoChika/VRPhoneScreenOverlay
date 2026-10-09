using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class ScrcpyControlProtocolWriterTests
{
    [Theory]
    [InlineData(PhoneInputCommandKind.TurnDisplayOff, 0)]
    [InlineData(PhoneInputCommandKind.MaintainDisplayOff, 0)]
    [InlineData(PhoneInputCommandKind.RestoreDisplayPower, 1)]
    public void PhysicalDisplayPowerUsesScrcpyDisplayMessageRatherThanAndroidSleepKey(PhoneInputCommandKind kind, byte power)
    {
        byte[] buffer = new byte[32];
        int length = ScrcpyControlProtocolWriter.Serialize(SystemCommand(kind), buffer);
        Assert.Equal(new byte[] { 10, power }, buffer[..length]);
    }

    [Fact]
    public void GuardedWakeDelegatesToDeviceWithoutAnyClientPanelOnOrWakeKeySequence()
    {
        byte[] buffer = new byte[32];
        int length = ScrcpyControlProtocolWriter.Serialize(SystemCommand(PhoneInputCommandKind.WakeScreenWhileDisplayOff), buffer);
        Assert.Equal(1, length);
        Assert.Equal(new byte[] { 128 }, buffer[..length]);
    }
    [Fact]
    public void SerializesTouchDownUsingScrcpy41WireFormat()
    {
        PhoneInputCommand command = new(
            7,
            PhoneInputCommandKind.PointerDown,
            0x1234567887654321,
            100f / 1079,
            200f / 1919,
            1080,
            1920,
            DateTimeOffset.UtcNow);
        byte[] buffer = new byte[32];

        int length = ScrcpyControlProtocolWriter.Serialize(command, buffer);

        Assert.Equal(32, length);
        Assert.Equal(
            new byte[]
            {
                0x02,
                0x00,
                0x12, 0x34, 0x56, 0x78, 0x87, 0x65, 0x43, 0x21,
                0x00, 0x00, 0x00, 0x64,
                0x00, 0x00, 0x00, 0xc8,
                0x04, 0x38,
                0x07, 0x80,
                0xff, 0xff,
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00,
            },
            buffer[..length].ToArray());
    }

    [Theory]
    [InlineData(PhoneInputCommandKind.Back, 4)]
    [InlineData(PhoneInputCommandKind.Home, 3)]
    [InlineData(PhoneInputCommandKind.RecentApps, 187)]
    [InlineData(PhoneInputCommandKind.Screenshot, 120)]
    public void SerializesSystemKeyAsDownThenUp(
        PhoneInputCommandKind kind,
        int keycode)
    {
        PhoneInputCommand command = SystemCommand(kind);
        Span<byte> buffer = stackalloc byte[32];

        int length = ScrcpyControlProtocolWriter.Serialize(command, buffer);

        Assert.Equal(28, length);
        Assert.Equal(0, buffer[0]);
        Assert.Equal(0, buffer[1]);
        Assert.Equal(keycode, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(buffer[2..6]));
        Assert.Equal(0, buffer[14]);
        Assert.Equal(1, buffer[15]);
        Assert.Equal(keycode, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(buffer[16..20]));
    }

    [Fact]
    public void SerializesControlPanelAsExpandSettingsMessage()
    {
        Span<byte> buffer = stackalloc byte[32];

        int length = ScrcpyControlProtocolWriter.Serialize(
            SystemCommand(PhoneInputCommandKind.OpenControlPanel),
            buffer);

        Assert.Equal(1, length);
        Assert.Equal(6, buffer[0]);
    }

    [Fact]
    public void SerializesScrollUsingScrcpy41WireFormat()
    {
        PhoneInputCommand command = new(
            9,
            PhoneInputCommandKind.Scroll,
            -2,
            260f / 1079,
            1026f / 1919,
            1080,
            1920,
            DateTimeOffset.UtcNow,
            -16);
        Span<byte> buffer = stackalloc byte[32];

        int length = ScrcpyControlProtocolWriter.Serialize(command, buffer);

        Assert.Equal(21, length);
        Assert.Equal(
            new byte[]
            {
                0x03,
                0x00, 0x00, 0x01, 0x04,
                0x00, 0x00, 0x04, 0x02,
                0x04, 0x38,
                0x07, 0x80,
                0x00, 0x00,
                0x80, 0x00,
                0x00, 0x00, 0x00, 0x00,
            },
            buffer[..length].ToArray());
    }

    [Fact]
    public void SerializesWakeScreenAsIdempotentDisplayPowerOn()
    {
        Span<byte> buffer = stackalloc byte[32];

        int length = ScrcpyControlProtocolWriter.Serialize(
            SystemCommand(PhoneInputCommandKind.WakeScreen),
            buffer);

        Assert.Equal(2, length);
        Assert.Equal(new byte[] { 10, 1 }, buffer[..length].ToArray());
    }

    [Fact]
    public void SerializesUserActivityAsCustomizedStatelessHeartbeat()
    {
        Span<byte> buffer = stackalloc byte[32];

        int length = ScrcpyControlProtocolWriter.Serialize(
            SystemCommand(PhoneInputCommandKind.UserActivity),
            buffer);

        Assert.Equal(1, length);
        Assert.Equal(127, buffer[0]);
    }

    [Theory]
    [InlineData(0, 7)]
    [InlineData(5, 12)]
    [InlineData(9, 16)]
    public void SerializesUnlockDigitWithoutTextOrClipboard(int digit, int keycode)
    {
        PhoneInputCommand command = SystemCommand(PhoneInputCommandKind.UnlockDigit) with
        {
            UnlockDigit = digit,
        };
        Span<byte> buffer = stackalloc byte[32];

        int length = ScrcpyControlProtocolWriter.Serialize(command, buffer);

        Assert.Equal(28, length);
        Assert.Equal(keycode, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(buffer[2..6]));
        Assert.Equal(keycode, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(buffer[16..20]));
    }

    [Theory]
    [InlineData(PhoneInputCommandKind.UnlockBackspace, 67)]
    [InlineData(PhoneInputCommandKind.UnlockConfirm, 66)]
    public void SerializesUnlockControlKeys(PhoneInputCommandKind kind, int keycode)
    {
        Span<byte> buffer = stackalloc byte[32];

        int length = ScrcpyControlProtocolWriter.Serialize(SystemCommand(kind), buffer);

        Assert.Equal(28, length);
        Assert.Equal(keycode, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(buffer[2..6]));
    }

    [Fact]
    public void RejectsUnlockDigitOutsideNumericRange()
    {
        PhoneInputCommand command = SystemCommand(PhoneInputCommandKind.UnlockDigit) with
        {
            UnlockDigit = 10,
        };
        byte[] buffer = new byte[32];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ScrcpyControlProtocolWriter.Serialize(command, buffer));
    }

    private static PhoneInputCommand SystemCommand(PhoneInputCommandKind kind) => new(
        1,
        kind,
        -2,
        0,
        0,
        0,
        0,
        DateTimeOffset.UtcNow);
}
