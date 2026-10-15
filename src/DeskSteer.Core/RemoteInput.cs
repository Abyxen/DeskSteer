namespace DeskSteer.Core;

public enum RemoteInputKind { Move = 1, ButtonDown = 2, ButtonUp = 3, Wheel = 4, KeyDown = 5, KeyUp = 6 }

public sealed record RemoteInputCommand(RemoteInputKind Kind, int X = 0, int Y = 0, int Code = 0, int Delta = 0);

public interface IRemoteInputSink
{
    void Apply(RemoteInputCommand command);
    void ReleaseAll();
}

public static class RemoteInputPolicy
{
    public static void Validate(RemoteInputCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var coordinates = command.X is >= 0 and <= 65535 && command.Y is >= 0 and <= 65535;
        var valid = command.Kind switch
        {
            RemoteInputKind.Move => coordinates && command.Code == 0 && command.Delta == 0,
            RemoteInputKind.ButtonDown or RemoteInputKind.ButtonUp => coordinates && command.Code is >= 1 and <= 3 && command.Delta == 0,
            RemoteInputKind.Wheel => coordinates && command.Code == 0 && command.Delta is >= -1200 and <= 1200 && command.Delta != 0 && command.Delta % 120 == 0,
            RemoteInputKind.KeyDown or RemoteInputKind.KeyUp => command.X == 0 && command.Y == 0 && command.Delta == 0 && IsAllowedKey(command.Code),
            _ => false
        };
        if (!valid) throw new InvalidDataException("Unsupported or out-of-range remote input command.");
    }

    public static bool IsAllowedKey(int code) => code is 8 or 9 or 13 or >= 16 and <= 20 or 27 or >= 32 and <= 46 or >= 48 and <= 93 or >= 96 and <= 123 or 144 or 145 or >= 160 and <= 165 or >= 186 and <= 192 or >= 219 and <= 222 or 226;
}
