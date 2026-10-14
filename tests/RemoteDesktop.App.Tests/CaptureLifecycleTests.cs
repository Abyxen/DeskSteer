using System.Runtime.CompilerServices;
using System.Windows;
using RemoteDesktop.Core;
using Xunit;

namespace RemoteDesktop.App.Tests;

public sealed class CaptureLifecycleTests
{
    [Fact]
    public Task CancelledSelectionRestoresIdleControlsWithoutCapture() => WpfHost.RunAsync(async () =>
    {
        var selection = new ControlledSelection();
        var view = new CaptureView(selection, new Diagnostics());
        var owner = new Window();
        var pending = view.SelectAsync(owner);
        Assert.False(((System.Windows.Controls.Button)view.FindName("SelectButton")).IsEnabled);
        await view.StopAsync();
        selection.Complete(null);
        await pending;
        Assert.Null(view.PreviewSource);
        Assert.DoesNotContain("CAPTURE ACTIVE", view.StatusText);
        Assert.True(((System.Windows.Controls.Button)view.FindName("SelectButton")).IsEnabled);
        Assert.False(((System.Windows.Controls.Button)view.FindName("StopButton")).IsEnabled);
    });

    [Fact]
    public Task LatePickerResultIsDisposedAfterStopWithoutShowingPixels() => WpfHost.RunAsync(async () =>
    {
        var selection = new ControlledSelection();
        var view = new CaptureView(selection, new Diagnostics());
        var capture = new TestCapture();
        var pending = view.SelectAsync(new Window());
        await view.StopAsync();
        selection.Complete(capture);
        await pending;
        Assert.Equal(1, capture.DisposeCount);
        Assert.Equal(0, capture.EnumerationCount);
        Assert.Null(view.PreviewSource);
        await view.StopAsync();
        Assert.Equal(1, capture.DisposeCount);
    });

    [Fact]
    public Task PickerFailureRestoresControlsAndShowsActionableStatus() => WpfHost.RunAsync(async () =>
    {
        var selection = new ControlledSelection();
        var view = new CaptureView(selection, new Diagnostics());
        var pending = view.SelectAsync(new Window());
        selection.Fail(new UnauthorizedAccessException("Test permission failure"));
        await pending;
        Assert.Contains("Could not start capture", view.StatusText);
        Assert.Null(view.PreviewSource);
        Assert.True(((System.Windows.Controls.Button)view.FindName("SelectButton")).IsEnabled);
        Assert.False(((System.Windows.Controls.Button)view.FindName("StopButton")).IsEnabled);
        await view.StopAsync();
    });

    [Fact]
    public Task RepeatedPreviewStopDisposesEachCaptureAndClearsPixels() => WpfHost.RunAsync(async () =>
    {
        var selection = new ControlledSelection();
        var view = new CaptureView(selection, new Diagnostics());
        for (var iteration = 0; iteration < 4; iteration++)
        {
            selection.Reset();
            var capture = new TestCapture();
            var pending = view.SelectAsync(new Window());
            selection.Complete(capture);
            await pending;
            await WaitFor(() => view.PreviewSource != null);
            await view.StopAsync();
            await view.StopAsync();
            Assert.Null(view.PreviewSource);
            Assert.Equal(1, capture.DisposeCount);
            Assert.Equal(1, capture.EnumerationCount);
        }
    });

    private static async Task WaitFor(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class ControlledSelection : ICaptureSelection
    {
        private TaskCompletionSource<IScreenCapture?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsSupported => true;
        // A platform picker may finish after cancellation; exercise that race explicitly.
        public Task<IScreenCapture?> SelectAsync(Window owner, CancellationToken cancellationToken) => result.Task;
        public void Complete(IScreenCapture? capture) => result.SetResult(capture);
        public void Fail(Exception error) => result.SetException(error);
        public void Reset() => result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class TestCapture : IScreenCapture
    {
        public int DisposeCount { get; private set; }
        public int EnumerationCount { get; private set; }
        public string DisplayName => "test-owned source";
        public CaptureMetrics Metrics => new(1, 0, 0, 1);
        public async IAsyncEnumerable<ScreenFrame> FramesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            EnumerationCount++;
            yield return new ScreenFrame(1, 1, [20, 40, 60, 255], TimeSpan.Zero);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
    }
}
