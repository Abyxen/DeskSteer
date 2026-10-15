using System.Buffers.Binary;
using DeskSteer.Core;
using Xunit;

namespace DeskSteer.Transport.Tests;

public sealed class InputProtocolTests
{
    [Fact]
    public void SupportedInputRoundTripsInExactlyTwentyBigEndianBytes()
    {
        foreach (var command in new[]
        {
            new RemoteInputCommand(RemoteInputKind.Move, 65535, 32768),
            new RemoteInputCommand(RemoteInputKind.ButtonDown, 123, 456, 1),
            new RemoteInputCommand(RemoteInputKind.ButtonUp, 123, 456, 3),
            new RemoteInputCommand(RemoteInputKind.Wheel, 65535, 0, Delta: -1200),
            new RemoteInputCommand(RemoteInputKind.KeyDown, Code: 65),
            new RemoteInputCommand(RemoteInputKind.KeyUp, Code: 165)
        })
        {
            var payload = ProtocolCodec.EncodeInput(command);
            Assert.Equal(20, payload.Length);
            Assert.Equal((int)command.Kind, BinaryPrimitives.ReadInt32BigEndian(payload));
            Assert.Equal(command.X, BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(4)));
            Assert.Equal(command.Delta, BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(16)));
            Assert.Equal(command, ProtocolCodec.DecodeInput(payload));
        }
    }

    [Fact]
    public void EveryVirtualKeyIsCheckedAgainstTheExplicitAllowlist()
    {
        for (var code = -1; code <= 256; code++)
        {
            var expected = code is 8 or 9 or 13 or >= 16 and <= 20 or 27 or >= 32 and <= 46 or >= 48 and <= 93 or >= 96 and <= 123 or 144 or 145 or >= 160 and <= 165 or >= 186 and <= 192 or >= 219 and <= 222 or 226;
            foreach (var kind in new[] { RemoteInputKind.KeyDown, RemoteInputKind.KeyUp })
            {
                var command = new RemoteInputCommand(kind, Code: code);
                if (expected) Assert.Equal(command, ProtocolCodec.DecodeInput(ProtocolCodec.EncodeInput(command)));
                else Assert.Throws<InvalidDataException>(() => ProtocolCodec.EncodeInput(command));
            }
        }
    }

    [Fact]
    public void InvalidKindsCoordinatesButtonsWheelsAndKeyFieldsAreRejectedInBothDirections()
    {
        foreach (var command in new[]
        {
            new RemoteInputCommand((RemoteInputKind)0), new RemoteInputCommand((RemoteInputKind)7),
            new RemoteInputCommand(RemoteInputKind.Move, -1), new RemoteInputCommand(RemoteInputKind.Move, Y: 65536),
            new RemoteInputCommand(RemoteInputKind.Move, Code: 1), new RemoteInputCommand(RemoteInputKind.Move, Delta: 120),
            new RemoteInputCommand(RemoteInputKind.ButtonDown, Code: 0), new RemoteInputCommand(RemoteInputKind.ButtonUp, Code: 4),
            new RemoteInputCommand(RemoteInputKind.ButtonDown, Code: 1, Delta: 120),
            new RemoteInputCommand(RemoteInputKind.Wheel), new RemoteInputCommand(RemoteInputKind.Wheel, Delta: 1),
            new RemoteInputCommand(RemoteInputKind.Wheel, Delta: 1320), new RemoteInputCommand(RemoteInputKind.Wheel, Delta: -1320),
            new RemoteInputCommand(RemoteInputKind.Wheel, Code: 1, Delta: 120),
            new RemoteInputCommand(RemoteInputKind.KeyDown, X: 1, Code: 65),
            new RemoteInputCommand(RemoteInputKind.KeyUp, Y: 1, Code: 65),
            new RemoteInputCommand(RemoteInputKind.KeyUp, Code: 65, Delta: 120)
        })
        {
            Assert.Throws<InvalidDataException>(() => ProtocolCodec.EncodeInput(command));
            var payload = new byte[20];
            BinaryPrimitives.WriteInt32BigEndian(payload, (int)command.Kind);
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4), command.X);
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(8), command.Y);
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(12), command.Code);
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(16), command.Delta);
            Assert.Throws<InvalidDataException>(() => ProtocolCodec.DecodeInput(payload));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(19)]
    [InlineData(21)]
    [InlineData(int.MaxValue)]
    public async Task InputLengthIsRejectedFromHeaderBeforePayloadAllocation(int size)
    {
        var header = new byte[16]; "PRDT"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), ProtocolCodec.Version);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), (ushort)MessageType.RemoteInput);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), (uint)size);
        using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<InvalidDataException>(() => ProtocolCodec.ReadAsync(stream, 1));
        Assert.Equal(16, stream.Position);
    }

    [Fact]
    public async Task VersionOneViewingPeersAreRejectedAndControlMessagesHaveNoPayload()
    {
        Assert.Equal(2, ProtocolCodec.Version);
        using var stream = new MemoryStream();
        await ProtocolCodec.WriteAsync(stream, new(MessageType.RequestControl, 1, []));
        var header = stream.ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), 1);
        using var oldPeer = new MemoryStream(header);
        await Assert.ThrowsAsync<InvalidDataException>(() => ProtocolCodec.ReadAsync(oldPeer, 1));
        foreach (var type in new[] { MessageType.RequestControl, MessageType.ControlGranted, MessageType.ControlRevoked, MessageType.ReleaseControl })
        {
            using var invalid = new MemoryStream();
            await Assert.ThrowsAsync<InvalidDataException>(() => ProtocolCodec.WriteAsync(invalid, new(type, 1, [0])));
            Assert.Equal(0, invalid.Length);
        }
    }
}
