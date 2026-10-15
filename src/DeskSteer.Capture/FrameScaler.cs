using DeskSteer.Core;

namespace DeskSteer.Capture;

internal static class FrameScaler
{
    // Deliberately simple nearest-neighbour preview. The transport encoder is a later milestone.
    internal static ScreenFrame Scale(int width, int height, byte[] pixels, TimeSpan timestamp)
    {
        if (width <= 0 || height <= 0 || pixels.Length != checked(width * height * 4)) throw new ArgumentException("Invalid BGRA frame.");
        var scale = Math.Min(1.0, Math.Min(1280.0 / width, 720.0 / height));
        if (scale == 1) return new(width, height, pixels, timestamp);
        var outputWidth = Math.Max(1, (int)(width * scale));
        var outputHeight = Math.Max(1, (int)(height * scale));
        var output = new byte[outputWidth * outputHeight * 4];
        for (var y = 0; y < outputHeight; y++)
        for (var x = 0; x < outputWidth; x++)
        {
            var source = ((y * height / outputHeight) * width + x * width / outputWidth) * 4;
            var destination = (y * outputWidth + x) * 4;
            pixels.AsSpan(source, 4).CopyTo(output.AsSpan(destination, 4));
        }
        return new(outputWidth, outputHeight, output, timestamp);
    }
}
