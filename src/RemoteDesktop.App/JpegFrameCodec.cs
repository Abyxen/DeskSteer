using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RemoteDesktop.Core;

namespace RemoteDesktop.App;

public static class JpegFrameCodec
{
    public const int MaxBytes = JpegFrameHeader.MaxBytes;
    public static EncodedFrame Encode(ScreenFrame frame)
    {
        ValidateDimensions(frame.Width, frame.Height);
        if (frame.BgraPixels.Length != checked(frame.Width * frame.Height * 4)) throw new InvalidDataException("Invalid pixel buffer.");
        var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.BgraPixels, frame.Width * 4);
        bitmap.Freeze();
        var encoder = new JpegBitmapEncoder { QualityLevel = 75 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream);
        if (stream.Length > MaxBytes) throw new InvalidDataException("Encoded frame exceeds the limit.");
        return new(frame.Width, frame.Height, frame.SystemRelativeTime.Ticks, stream.ToArray());
    }
    public static BitmapSource Decode(EncodedFrame frame)
    {
        ValidateDimensions(frame.Width, frame.Height);
        if (frame.Jpeg.Length is < 4 or > MaxBytes) throw new InvalidDataException("Invalid encoded frame size.");
        var dimensions = ReadDimensions(frame.Jpeg);
        if (dimensions.Width != frame.Width || dimensions.Height != frame.Height) throw new InvalidDataException("JPEG dimensions disagree with the frame.");
        using var stream = new MemoryStream(frame.Jpeg, writable: false);
        var decoder = new JpegBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count != 1) throw new InvalidDataException("Expected one JPEG frame.");
        var bitmap = decoder.Frames[0];
        if (bitmap.PixelWidth != frame.Width || bitmap.PixelHeight != frame.Height) throw new InvalidDataException("Decoded dimensions disagree.");
        bitmap.Freeze(); return bitmap;
    }
    public static (int Width, int Height) ReadDimensions(ReadOnlySpan<byte> data) => JpegFrameHeader.ReadDimensions(data);
    private static void ValidateDimensions(int width, int height)
    {
        if (width is <= 0 or > 1280 || height is <= 0 or > 720) throw new InvalidDataException("Frame dimensions exceed the preview limit.");
    }
}

