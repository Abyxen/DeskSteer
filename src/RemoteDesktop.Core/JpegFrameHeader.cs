using System.Buffers.Binary;

namespace RemoteDesktop.Core;

/// <summary>Validates the bounded baseline JPEG profile emitted by our encoder before decoding.</summary>
public static class JpegFrameHeader
{
    public const int MaxBytes = 2 * 1024 * 1024 - 16;
    public static (int Width, int Height) ReadDimensions(ReadOnlySpan<byte> data)
    {
        if (data.Length is < 4 or > MaxBytes || data[0] != 0xff || data[1] != 0xd8)
            throw new InvalidDataException("Expected a bounded JPEG image.");
        var position = 2;
        (int Width, int Height)? dimensions = null;
        while (position < data.Length)
        {
            if (data[position++] != 0xff) throw new InvalidDataException("Invalid JPEG marker.");
            while (position < data.Length && data[position] == 0xff) position++;
            if (position >= data.Length) break;
            var marker = data[position++];
            if (marker == 0x01) continue;
            if (marker is not (0xc0 or 0xc4 or 0xda or 0xdb or 0xdd or 0xfe) && marker is not (>= 0xe0 and <= 0xef))
                throw new InvalidDataException("Unsupported JPEG profile or marker.");
            if (position + 2 > data.Length) break;
            var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position, 2));
            if (length < 2 || position + length > data.Length) throw new InvalidDataException("Truncated JPEG segment.");
            if (marker == 0xc0)
            {
                if (dimensions != null || length < 8 || data[position + 2] != 8)
                    throw new InvalidDataException("Expected one eight-bit baseline JPEG frame.");
                var height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + 5, 2));
                var components = data[position + 7];
                if (width is < 1 or > 1280 || height is < 1 or > 720 || components is not (1 or 3) || length != 8 + components * 3)
                    throw new InvalidDataException("JPEG dimensions or components exceed the profile.");
                dimensions = (width, height);
            }
            if (marker == 0xda)
            {
                if (dimensions == null || length < 6) throw new InvalidDataException("JPEG frame header is missing.");
                var components = data[position + 2];
                if (components is not (1 or 3) || length != 6 + components * 2 || data[position + length - 3] != 0 || data[position + length - 2] != 63 || data[position + length - 1] != 0)
                    throw new InvalidDataException("Expected one baseline JPEG scan.");
                ValidateScan(data, position + length);
                return dimensions.Value;
            }
            position += length;
        }
        throw new InvalidDataException("JPEG scan is missing.");
    }
    private static void ValidateScan(ReadOnlySpan<byte> data, int position)
    {
        while (position < data.Length)
        {
            if (data[position++] != 0xff) continue;
            while (position < data.Length && data[position] == 0xff) position++;
            if (position >= data.Length) break;
            var marker = data[position++];
            if (marker == 0x00 || marker is >= 0xd0 and <= 0xd7) continue;
            if (marker == 0xd9 && position == data.Length) return;
            throw new InvalidDataException("JPEG contains another scan, frame or trailing image.");
        }
        throw new InvalidDataException("JPEG end marker is missing.");
    }
}
