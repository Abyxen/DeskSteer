using System.Buffers.Binary;
using DeskSteer.Core;
using DeskSteer.Transport;
using Xunit;

namespace DeskSteer.Transport.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public async Task ActualJpegRoundTripsWithBigEndianMetadataAndFraming()
    {
        var original = FrameFixture.Create();
        var payload = ProtocolCodec.EncodeFrame(original);
        Assert.Equal(original.Width, BinaryPrimitives.ReadInt32BigEndian(payload));
        Assert.Equal(original.TimestampTicks, BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(8)));
        using var stream = new MemoryStream();
        await ProtocolCodec.WriteAsync(stream, new(MessageType.Frame, 1, payload));
        stream.Position = 0;
        var message = await ProtocolCodec.ReadAsync(stream, 1);
        Assert.Equal(MessageType.Frame, message.Type);
        var decoded = ProtocolCodec.DecodeFrame(message.Payload);
        Assert.Equal(original.Width, decoded.Width);
        Assert.Equal(original.Height, decoded.Height);
        Assert.Equal(original.TimestampTicks, decoded.TimestampTicks);
        Assert.Equal(original.Jpeg, decoded.Jpeg);
        Assert.Equal(stream.Length, stream.Position);
    }

    [Theory]
    [InlineData("magic")]
    [InlineData("version")]
    [InlineData("type")]
    [InlineData("zero sequence")]
    [InlineData("wrong sequence")]
    [InlineData("oversized frame")]
    [InlineData("control payload")]
    public async Task RejectsInvalidHeaderBeforeReadingPayload(string corruption)
    {
        using var valid = new MemoryStream();
        await ProtocolCodec.WriteAsync(valid, new(MessageType.Heartbeat, 1, []));
        var header = valid.ToArray();
        switch (corruption)
        {
            case "magic": header[0] = 0; break;
            case "version": BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)(ProtocolCodec.Version + 1)); break;
            case "type": BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), 65535); break;
            case "zero sequence": BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), 0); break;
            case "wrong sequence": BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), 2); break;
            case "oversized frame":
                BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), (ushort)MessageType.Frame);
                BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), uint.MaxValue);
                break;
            case "control payload": BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), 1); break;
        }
        using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<InvalidDataException>(() => ProtocolCodec.ReadAsync(stream, 1));
    }

    [Fact]
    public async Task TruncatedHeaderAndPayloadAreRejected()
    {
        using var valid = new MemoryStream();
        await ProtocolCodec.WriteAsync(valid, new(MessageType.Frame, 1, ProtocolCodec.EncodeFrame(FrameFixture.Create())));
        var bytes = valid.ToArray();
        foreach (var length in new[] { 0, 15, 16, bytes.Length - 1 })
        {
            using var stream = new MemoryStream(bytes[..length]);
            await Assert.ThrowsAsync<EndOfStreamException>(() => ProtocolCodec.ReadAsync(stream, 1));
        }
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1281, 1, 0)]
    [InlineData(2, 721, 0)]
    [InlineData(2, 1, -1)]
    [InlineData(3, 1, 0)]
    public void RejectsInvalidFrameBoundsTimestampsAndJpegMetadata(int width, int height, long timestamp)
    {
        var original = FrameFixture.Create();
        Assert.Throws<InvalidDataException>(() => ProtocolCodec.EncodeFrame(original with { Width = width, Height = height, TimestampTicks = timestamp }));
    }

    [Fact]
    public void RejectsTruncatedOrOversizedJpegAndInvalidAuthenticationNames()
    {
        var original = FrameFixture.Create();
        Assert.Throws<InvalidDataException>(() => ProtocolCodec.EncodeFrame(original with { Jpeg = original.Jpeg[..^1] }));
        Assert.Throws<InvalidDataException>(() => ProtocolCodec.EncodeFrame(original with { Jpeg = new byte[ProtocolCodec.MaxPayloadLength] }));
        foreach (var name in new[] { "", " ", "viewer\nname", new string('a', 65) })
            Assert.Throws<InvalidDataException>(() => ProtocolCodec.EncodeAuthentication(new byte[32], name));
        Assert.Throws<ArgumentException>(() => ProtocolCodec.EncodeAuthentication(new byte[31], "viewer"));
        var payload = ProtocolCodec.EncodeAuthentication(new byte[32], "viewer");
        payload[32] = 255;
        Assert.Throws<InvalidDataException>(() => ProtocolCodec.DecodeAuthentication(payload));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("8.8.8.8")]
    [InlineData("172.15.1.2")]
    [InlineData("172.32.1.2")]
    [InlineData("localhost")]
    [InlineData("::1")]
    public void WildcardPublicHostnameAndIpv6EndpointsAreRejected(string address) => Assert.Throws<ArgumentException>(() => LanAddressPolicy.Parse(address));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.20.30.40")]
    [InlineData("172.16.1.2")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.2")]
    public void ExplicitPrivateIpv4EndpointsAreAllowed(string address) => Assert.Equal(address, LanAddressPolicy.Parse(address).ToString());
}
