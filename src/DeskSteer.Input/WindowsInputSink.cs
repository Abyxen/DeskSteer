using System.ComponentModel;
using System.Runtime.InteropServices;
using DeskSteer.Core;

namespace DeskSteer.Input;

public sealed class WindowsInputSink : IRemoteInputSink
{
    private readonly object gate = new();
    private readonly IInputBackend backend;
    private readonly DisplayGeometry geometry;
    private readonly HashSet<int> keys = [], buttons = [];
    private bool invalidated;
    internal WindowsInputSink(IInputBackend backend)
    {
        this.backend = backend; geometry = backend.GetGeometry(); geometry.Map(0, 0);
    }
    public static WindowsInputSink ForPrimaryDisplay() => new(new Win32InputBackend());

    public void Apply(RemoteInputCommand command)
    {
        RemoteInputPolicy.Validate(command);
        lock (gate)
        {
            if (invalidated) throw new InvalidOperationException("Display configuration changed. Start a new control session.");
            try
            {
                if (backend.GetGeometry() != geometry) throw new InvalidOperationException("Display configuration changed. Start a new control session.");
            }
            catch { invalidated = true; throw; }
            if (command.Kind is RemoteInputKind.KeyDown or RemoteInputKind.KeyUp)
            {
                var down = command.Kind == RemoteInputKind.KeyDown;
                // Never take ownership of a key that was already held locally.
                if (down && !keys.Contains(command.Code) && backend.IsDown(command.Code)) return;
                if (!down && !keys.Contains(command.Code)) return;
                Send(Key(command.Code, !down));
                if (down) keys.Add(command.Code); else keys.Remove(command.Code);
                return;
            }
            var (x, y) = geometry.Map(command.X, command.Y);
            uint action = 0, data = 0;
            if (command.Kind is RemoteInputKind.ButtonDown or RemoteInputKind.ButtonUp)
            {
                var down = command.Kind == RemoteInputKind.ButtonDown;
                if (down && !buttons.Contains(command.Code) && backend.IsDown(ButtonVk(command.Code))) return;
                if (!down && !buttons.Contains(command.Code)) return;
                action = ButtonFlag(command.Code, !down);
            }
            else if (command.Kind == RemoteInputKind.Wheel) { action = 0x0800; data = unchecked((uint)command.Delta); }
            // Movement and button/wheel action are one SendInput record, applied together.
            Send(new NativeInput { Type = 0, Data = new() { Mouse = new() { X = x, Y = y, Flags = 0xC001 | action, MouseData = data } } });
            if (command.Kind == RemoteInputKind.ButtonDown) buttons.Add(command.Code);
            if (command.Kind == RemoteInputKind.ButtonUp) buttons.Remove(command.Code);
        }
    }

    public void ReleaseAll()
    {
        lock (gate)
        {
            List<Exception> failures = [];
            // Releases intentionally work even after a display is unplugged: no pointer move.
            foreach (var code in keys.ToArray())
                try { Send(Key(code, true)); keys.Remove(code); } catch (Exception error) { failures.Add(error); }
            foreach (var code in buttons.ToArray())
                try { Send(new NativeInput { Data = new() { Mouse = new() { Flags = ButtonFlag(code, true) } } }); buttons.Remove(code); } catch (Exception error) { failures.Add(error); }
            if (failures.Count != 0) throw new AggregateException("Some remote input could not be released.", failures);
        }
    }
    private void Send(NativeInput input)
    {
        if (backend.Send(input) != 1) throw new Win32Exception(backend.LastError, "Windows did not accept the input. Elevated windows and secure desktops may block control.");
    }
    private static NativeInput Key(int code, bool up) => new() { Type = 1, Data = new() { Keyboard = new() { VirtualKey = (ushort)code, Flags = (up ? 2u : 0) | (IsExtended(code) ? 1u : 0) } } };
    internal static bool IsExtended(int code) => code is >= 0x21 and <= 0x2e or 0x5b or 0x5c or 0x5d or 0x6f or 0x90 or 0xa3 or 0xa5;
    private static int ButtonVk(int code) => code switch { 1 => 1, 2 => 2, 3 => 4, _ => throw new ArgumentOutOfRangeException(nameof(code)) };
    private static uint ButtonFlag(int code, bool up) => code switch { 1 => up ? 4u : 2u, 2 => up ? 16u : 8u, 3 => up ? 64u : 32u, _ => throw new ArgumentOutOfRangeException(nameof(code)) };
}

internal interface IInputBackend
{
    DisplayGeometry GetGeometry();
    bool IsDown(int virtualKey);
    uint Send(NativeInput input);
    int LastError { get; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeInput { public uint Type; public InputUnion Data; }
[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)] public MouseInput Mouse;
    [FieldOffset(0)] public KeyboardInput Keyboard;
    [FieldOffset(0)] public HardwareInput Hardware;
}
[StructLayout(LayoutKind.Sequential)]
internal struct MouseInput { public int X, Y; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
[StructLayout(LayoutKind.Sequential)]
internal struct KeyboardInput { public ushort VirtualKey, ScanCode; public uint Flags, Time; public nuint ExtraInfo; }
[StructLayout(LayoutKind.Sequential)]
internal struct HardwareInput { public uint Message; public ushort Low, High; }
