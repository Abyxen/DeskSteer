namespace DeskSteer.Input;

internal sealed record DisplayGeometry(int PrimaryLeft, int PrimaryTop, int PrimaryWidth, int PrimaryHeight,
    int VirtualLeft, int VirtualTop, int VirtualWidth, int VirtualHeight, string Topology)
{
    internal (int X, int Y) Map(int x, int y)
    {
        if (x is < 0 or > 65535 || y is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(x));
        if (PrimaryWidth < 1 || PrimaryHeight < 1 || VirtualWidth < 2 || VirtualHeight < 2 ||
            PrimaryLeft < VirtualLeft || PrimaryTop < VirtualTop ||
            (long)PrimaryLeft + PrimaryWidth > (long)VirtualLeft + VirtualWidth ||
            (long)PrimaryTop + PrimaryHeight > (long)VirtualTop + VirtualHeight)
            throw new InvalidOperationException("Display geometry is unavailable. Start a new control session.");
        // Convert to a primary-display pixel first, then the full virtual desktop.
        var pixelX = PrimaryLeft + (long)x * (PrimaryWidth - 1) / 65535;
        var pixelY = PrimaryTop + (long)y * (PrimaryHeight - 1) / 65535;
        return ((int)((pixelX - VirtualLeft) * 65535 / (VirtualWidth - 1)),
                (int)((pixelY - VirtualTop) * 65535 / (VirtualHeight - 1)));
    }
}
