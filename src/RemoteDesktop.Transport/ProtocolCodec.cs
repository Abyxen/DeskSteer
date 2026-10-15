using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using RemoteDesktop.Core;

namespace RemoteDesktop.Transport;

public enum MessageType : ushort { Authentication = 1, Approved = 2, Rejected = 3, Frame = 4, Heartbeat = 5, Disconnect = 6 }
public sealed record ProtocolMessage(MessageType Type, uint Sequence, byte[] Payload);

public static class ProtocolCodec
{
    public const int HeaderLength = 16;
    public const int MaxPayloadLength = 2 * 1024 * 1024;
    public const ushort Version = 1;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static async Task<ProtocolMessage> ReadAsync(Stream stream, uint expectedSequence, CancellationToken cancellationToken = default)
    {
        var header = new byte[HeaderLength];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        if (!header.AsSpan(0, 4).SequenceEqual("PRDT"u8) || BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)) != Version)
            throw new InvalidDataException("Unsupported protocol header.");
        var type = (MessageType)BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(6));
        var sequence = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));
        var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12));
        if (sequence == 0 || sequence != expectedSequence) throw new InvalidDataException("Out-of-order protocol message.");
        ValidateLength(type, length);
        // All header limits are checked before allocation or reading attacker-controlled bytes.
        var payload = new byte[(int)length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return new(type, sequence, payload);
    }

    public static async Task WriteAsync(Stream stream, ProtocolMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message.Payload);
        if (message.Sequence == 0) throw new InvalidDataException("Protocol sequence starts at one.");
        ValidateLength(message.Type, (uint)message.Payload.Length);
        var header = new byte[HeaderLength];
        "PRDT"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), Version);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), (ushort)message.Type);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), message.Sequence);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), (uint)message.Payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(message.Payload, cancellationToken).ConfigureAwait(false);
    }

    public static byte[] EncodeFrame(EncodedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame.Jpeg);
        ValidateFrame(frame.Width, frame.Height, frame.TimestampTicks, frame.Jpeg);
        var payload = new byte[16 + frame.Jpeg.Length];
        BinaryPrimitives.WriteInt32BigEndian(payload, frame.Width);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4), frame.Height);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(8), frame.TimestampTicks);
        frame.Jpeg.CopyTo(payload, 16);
        return payload;
    }

    public static EncodedFrame DecodeFrame(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ValidateLength(MessageType.Frame, (uint)payload.Length);
        var width = BinaryPrimitives.ReadInt32BigEndian(payload);
        var height = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(4));
        var timestamp = BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(8));
        ValidateFrame(width, height, timestamp, payload.AsSpan(16));
        return new(width, height, timestamp, payload.AsSpan(16).ToArray());
    }

    public static byte[] EncodeAuthentication(byte[] token, string displayName)
    {
        if (token.Length != 32) throw new ArgumentException("A pairing token is 32 bytes.", nameof(token));
        ValidateName(displayName);
        var name = StrictUtf8.GetBytes(displayName);
        var payload = new byte[32 + name.Length];
        token.CopyTo(payload, 0); name.CopyTo(payload, 32);
        return payload;
    }

    public static (byte[] Token, ViewerHello Hello) DecodeAuthentication(byte[] payload)
    {
        ValidateLength(MessageType.Authentication, (uint)payload.Length);
        string name;
        try { name = StrictUtf8.GetString(payload.AsSpan(32)); }
        catch (DecoderFallbackException error) { throw new InvalidDataException("Invalid viewer name.", error); }
        ValidateName(name);
        return (payload.AsSpan(0, 32).ToArray(), new(name));
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64)
            throw new InvalidDataException("Viewer name must contain 1–64 printable characters.");
        foreach (var rune in name.EnumerateRunes())
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                throw new InvalidDataException("Viewer name cannot contain control, formatting or line-separator characters.");
        try
        {
            // Strict encoding rejects unpaired UTF-16 surrogates without excluding
            // valid supplementary characters such as simple emoji.
            if (StrictUtf8.GetByteCount(name) > 256) throw new InvalidDataException("Viewer name is too long.");
        }
        catch (EncoderFallbackException error) { throw new InvalidDataException("Viewer name contains invalid Unicode.", error); }
    }

    private static void ValidateLength(MessageType type, uint length)
    {
        var valid = type switch
        {
            MessageType.Authentication => length is >= 33 and <= 288,
            MessageType.Frame => length is >= 20 and <= MaxPayloadLength,
            MessageType.Approved or MessageType.Rejected or MessageType.Heartbeat or MessageType.Disconnect => length == 0,
            _ => false
        };
        if (!valid) throw new InvalidDataException("Invalid protocol message type or size.");
    }

    private static void ValidateFrame(int width, int height, long timestamp, ReadOnlySpan<byte> jpeg)
    {
        if (width is < 1 or > 1280 || height is < 1 or > 720 || timestamp < 0 || jpeg.Length is < 4 or > MaxPayloadLength - 16)
            throw new InvalidDataException("Frame exceeds the preview limits.");
        // The transport and UI share one strict validator so the dimensions checked
        // before allocation cannot diverge from those accepted by the image decoder.
        var dimensions = JpegFrameHeader.ReadDimensions(jpeg);
        if (dimensions.Width != width || dimensions.Height != height)
            throw new InvalidDataException("JPEG dimensions do not match the bounded frame metadata.");
    }
}
