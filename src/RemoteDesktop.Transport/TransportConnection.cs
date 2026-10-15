using System.Net.Security;

namespace RemoteDesktop.Transport;

internal sealed class TransportConnection(SslStream stream, LanOptions options)
{
    private readonly SemaphoreSlim writes = new(1, 1);
    private uint nextRead = 1, nextWrite = 1;

    internal async Task<ProtocolMessage> ReadAsync(CancellationToken token, TimeSpan? timeout = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout ?? options.ReadWriteTimeout);
        try
        {
            if (nextRead == uint.MaxValue) throw new InvalidDataException("Protocol sequence exhausted.");
            var message = await ProtocolCodec.ReadAsync(stream, nextRead, deadline.Token).ConfigureAwait(false);
            nextRead++;
            return message;
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        { throw new TimeoutException("Peer did not send a complete message in time.", error); }
    }

    internal async Task WriteAsync(MessageType type, byte[] payload, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(options.ReadWriteTimeout);
        var entered = false;
        try
        {
            await writes.WaitAsync(deadline.Token).ConfigureAwait(false);
            entered = true;
            if (nextWrite == uint.MaxValue) throw new InvalidDataException("Protocol sequence exhausted.");
            await ProtocolCodec.WriteAsync(stream, new(type, nextWrite, payload), deadline.Token).ConfigureAwait(false);
            nextWrite++;
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        { throw new TimeoutException("Peer did not receive a complete message in time.", error); }
        finally { if (entered) writes.Release(); }
    }

    internal async Task HeartbeatsAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(options.HeartbeatInterval);
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            await WriteAsync(MessageType.Heartbeat, [], token).ConfigureAwait(false);
    }
}
