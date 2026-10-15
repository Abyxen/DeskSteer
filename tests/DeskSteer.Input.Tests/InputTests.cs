using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using DeskSteer.Core;
using DeskSteer.Input;
using Xunit;

namespace DeskSteer.Input.Tests;

public sealed class InputTests
{
    private static DisplayGeometry PrimaryOnly => new(0, 0, 1920, 1080, 0, 0, 1920, 1080, "primary");
    [Fact]
    public void NativeInputLayoutMatchesWindowsAbi()
    {
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf<NativeInput>());
        Assert.Equal(IntPtr.Size == 8 ? 8 : 4, Marshal.OffsetOf<NativeInput>(nameof(NativeInput.Data)).ToInt32());
        Assert.Equal(IntPtr.Size == 8 ? 32 : 24, Marshal.SizeOf<MouseInput>());
        Assert.Equal(IntPtr.Size == 8 ? 24 : 16, Marshal.SizeOf<KeyboardInput>());
    }
    [Fact]
    public void PrimaryEndpointsAndNegativeVirtualOriginMapInsidePrimaryDisplay()
    {
        Assert.Equal((0, 0), PrimaryOnly.Map(0, 0));
        Assert.Equal((65535, 65535), PrimaryOnly.Map(65535, 65535));
        var multiple = new DisplayGeometry(0, 0, 1920, 1080, -1920, -1080, 3840, 2160, "left+above");
        var first = multiple.Map(0, 0); var last = multiple.Map(65535, 65535);
        Assert.InRange(first.X, 32767, 32777); Assert.InRange(first.Y, 32767, 32785);
        Assert.Equal((65535, 65535), last);
        for (var x = 0; x <= 65535; x += 257)
        {
            var mapped = multiple.Map(x, x);
            Assert.InRange(mapped.X, first.X, last.X); Assert.InRange(mapped.Y, first.Y, last.Y);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => multiple.Map(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => multiple.Map(0, 65536));
        Assert.Throws<InvalidOperationException>(() => (multiple with { VirtualWidth = 1 }).Map(0, 0));
        Assert.Throws<InvalidOperationException>(() => (multiple with { PrimaryLeft = -3000 }).Map(0, 0));
        var rightHandDisplay = new DisplayGeometry(0, 0, 1920, 1080, 0, 0, 3840, 1080, "primary+right");
        Assert.InRange(rightHandDisplay.Map(65535, 65535).X, 32750, 32767);
    }
    [Theory]
    [InlineData(1, 2u, 4u)]
    [InlineData(2, 8u, 16u)]
    [InlineData(3, 32u, 64u)]
    public void ButtonActionsIncludeAtomicAbsoluteVirtualDesktopMove(int button, uint downFlag, uint upFlag)
    {
        var backend = new RecordingBackend(); var sink = new WindowsInputSink(backend);
        sink.Apply(new(RemoteInputKind.ButtonDown, 65535, 65535, button));
        sink.Apply(new(RemoteInputKind.ButtonUp, 0, 0, button));
        Assert.Equal(2, backend.Events.Count);
        Assert.Equal(0u, backend.Events[0].Type);
        Assert.Equal(0xC001u | downFlag, backend.Events[0].Data.Mouse.Flags);
        Assert.Equal(65535, backend.Events[0].Data.Mouse.X);
        Assert.Equal(0xC001u | upFlag, backend.Events[1].Data.Mouse.Flags);
        Assert.Equal(0, backend.Events[1].Data.Mouse.Y);
        sink.ReleaseAll(); Assert.Equal(2, backend.Events.Count);
    }
    [Fact]
    public void WheelKeepsSignedDeltaAndMovesToTheSpecifiedPosition()
    {
        var backend = new RecordingBackend(); var sink = new WindowsInputSink(backend);
        sink.Apply(new(RemoteInputKind.Wheel, 65535, 0, Delta: -240));
        var input = Assert.Single(backend.Events);
        Assert.Equal(0xC801u, input.Data.Mouse.Flags);
        Assert.Equal(-240, unchecked((int)input.Data.Mouse.MouseData));
        Assert.Equal(65535, input.Data.Mouse.X);
    }
    [Theory]
    [InlineData(0x41, false)]
    [InlineData(0xa3, true)]
    [InlineData(0xa5, true)]
    [InlineData(0x25, true)]
    [InlineData(0xa1, false)]
    public void KeyboardUsesVirtualKeysAndCorrectExtendedKeyUpFlags(int code, bool extended)
    {
        var backend = new RecordingBackend(); var sink = new WindowsInputSink(backend);
        sink.Apply(new(RemoteInputKind.KeyDown, Code: code));
        sink.Apply(new(RemoteInputKind.KeyUp, Code: code));
        Assert.Equal(1u, backend.Events[0].Type);
        Assert.Equal((ushort)code, backend.Events[0].Data.Keyboard.VirtualKey);
        Assert.Equal(extended ? 1u : 0, backend.Events[0].Data.Keyboard.Flags);
        Assert.Equal(extended ? 3u : 2u, backend.Events[1].Data.Keyboard.Flags);
    }
    [Fact]
    public void ReleaseAllReleasesOnlyOwnedPressesAndWorksAfterGeometryChanges()
    {
        var backend = new RecordingBackend(); backend.LocallyDown.UnionWith([0x42, 2]);
        var sink = new WindowsInputSink(backend);
        sink.Apply(new(RemoteInputKind.KeyDown, Code: 0x42));
        sink.Apply(new(RemoteInputKind.ButtonDown, Code: 2));
        sink.Apply(new(RemoteInputKind.KeyUp, Code: 0x42));
        sink.Apply(new(RemoteInputKind.ButtonUp, Code: 2));
        Assert.Empty(backend.Events);
        sink.Apply(new(RemoteInputKind.KeyDown, Code: 0x41));
        sink.Apply(new(RemoteInputKind.ButtonDown, Code: 1));
        backend.Geometry = backend.Geometry with { Topology = "unplugged" };
        Assert.Throws<InvalidOperationException>(() => sink.Apply(new(RemoteInputKind.Move)));
        sink.ReleaseAll();
        Assert.Equal(4, backend.Events.Count);
        Assert.Equal(2u, backend.Events[2].Data.Keyboard.Flags);
        Assert.Equal(4u, backend.Events[3].Data.Mouse.Flags); // release never moves the pointer
        sink.ReleaseAll(); Assert.Equal(4, backend.Events.Count);
        backend.Geometry = PrimaryOnly;
        Assert.Throws<InvalidOperationException>(() => sink.Apply(new(RemoteInputKind.Move)));
    }
    [Fact]
    public void RepeatedKeyDownProducesRepeatsButOneReleaseAndFailureKeepsReleaseOwnership()
    {
        var backend = new RecordingBackend(); var sink = new WindowsInputSink(backend);
        sink.Apply(new(RemoteInputKind.KeyDown, Code: 65)); sink.Apply(new(RemoteInputKind.KeyDown, Code: 65));
        backend.FailNext = true;
        Assert.Throws<Win32Exception>(() => sink.Apply(new(RemoteInputKind.KeyUp, Code: 65)));
        sink.ReleaseAll();
        Assert.Equal(3, backend.Events.Count);
        Assert.Equal(2u, backend.Events[^1].Data.Keyboard.Flags);
    }
    [Fact]
    public void FailedPressDoesNotCreateOwnershipAndFailedReleaseIsRetried()
    {
        var backend = new RecordingBackend { FailNext = true }; var sink = new WindowsInputSink(backend);
        Assert.Throws<Win32Exception>(() => sink.Apply(new(RemoteInputKind.ButtonDown, Code: 1)));
        sink.ReleaseAll(); Assert.Empty(backend.Events);
        sink.Apply(new(RemoteInputKind.KeyDown, Code: 65)); sink.Apply(new(RemoteInputKind.ButtonDown, Code: 1));
        backend.FailNext = true;
        Assert.Throws<AggregateException>(sink.ReleaseAll);
        Assert.Equal(3, backend.Events.Count); // mouse release still attempted after key release failure
        sink.ReleaseAll(); Assert.Equal(4, backend.Events.Count);
        Assert.Equal(2u, backend.Events[^1].Data.Keyboard.Flags);
    }
    [Fact]
    public void InvalidCommandsNeverReachNativeBackend()
    {
        var backend = new RecordingBackend(); var sink = new WindowsInputSink(backend);
        foreach (var command in new[] { new RemoteInputCommand(RemoteInputKind.Move, -1), new(RemoteInputKind.ButtonDown, Code: 4), new(RemoteInputKind.KeyDown, Code: 1), new(RemoteInputKind.Wheel, Delta: 1), new((RemoteInputKind)100) })
            Assert.Throws<InvalidDataException>(() => sink.Apply(command));
        Assert.Empty(backend.Events);
    }
    [Fact]
    public async Task ConcurrentCallersNeverEnterNativeSendTogether()
    {
        var backend = new RecordingBackend { ProbeConcurrency = true }; var sink = new WindowsInputSink(backend);
        await Task.WhenAll(Enumerable.Range(0, 16).Select(index => Task.Run(() =>
        {
            for (var move = 0; move < 16; move++) sink.Apply(new(RemoteInputKind.Move, index * 100, move * 100));
            sink.ReleaseAll();
        })));
        Assert.Equal(256, backend.Events.Count);
        Assert.Equal(1, backend.MaxConcurrentSends);
    }
    private sealed class RecordingBackend : IInputBackend
    {
        public DisplayGeometry Geometry { get; set; } = PrimaryOnly;
        public HashSet<int> LocallyDown { get; } = [];
        public List<NativeInput> Events { get; } = [];
        public bool FailNext { get; set; }
        public bool ProbeConcurrency { get; set; }
        private int active;
        public int MaxConcurrentSends { get; private set; }
        public int LastError => 5;
        public DisplayGeometry GetGeometry() => Geometry;
        public bool IsDown(int code) => LocallyDown.Contains(code);
        public uint Send(NativeInput input)
        {
            var concurrent = Interlocked.Increment(ref active);
            try
            {
                MaxConcurrentSends = Math.Max(concurrent, MaxConcurrentSends);
                if (ProbeConcurrency) Thread.SpinWait(5000);
                if (FailNext) { FailNext = false; return 0; }
                Events.Add(input); return 1;
            }
            finally { Interlocked.Decrement(ref active); }
        }
    }
}
