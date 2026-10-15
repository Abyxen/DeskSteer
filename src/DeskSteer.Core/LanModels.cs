namespace DeskSteer.Core;

public sealed record EncodedFrame(int Width, int Height, long TimestampTicks, byte[] Jpeg);
public sealed record ViewerHello(string DisplayName);
public sealed record ConnectionRequest(string Address, int Port, string Fingerprint, string PairingCode, string ViewerName);
public enum LanSessionState { Listening, Authenticating, AwaitingApproval, Streaming, Closed, Failed }
