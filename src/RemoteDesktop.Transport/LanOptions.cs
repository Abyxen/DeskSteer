using System.Net;
using System.Net.Sockets;

namespace RemoteDesktop.Transport;

public sealed record LanOptions
{
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ApprovalTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ReadWriteTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan PairingLifetime { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromHours(1);
    public int MaxFailedAttempts { get; init; } = 5;

    internal void Validate()
    {
        foreach (var duration in new[] { HandshakeTimeout, ApprovalTimeout, ReadWriteTimeout, HeartbeatInterval, PairingLifetime, SessionLifetime })
            if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(24)) throw new ArgumentOutOfRangeException(nameof(LanOptions), "Timeouts must be positive and at most 24 hours.");
        if (HeartbeatInterval >= ReadWriteTimeout) throw new ArgumentException("Heartbeat interval must be shorter than the read/write timeout.");
        if (MaxFailedAttempts is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(MaxFailedAttempts));
    }
}

public static class LanAddressPolicy
{
    public static bool IsAllowed(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 127 || bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168);
    }

    public static IPAddress Parse(string address)
    {
        if (!IPAddress.TryParse(address, out var parsed) || !IsAllowed(parsed))
            throw new ArgumentException("Use a private IPv4 or loopback address. Wildcard, public, hostname and IPv6 endpoints are unavailable.", nameof(address));
        return parsed;
    }
}
