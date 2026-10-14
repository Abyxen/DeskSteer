using Microsoft.Extensions.Logging;

namespace RemoteDesktop.App;

// Allowlisted event IDs reach the export; message values and exception details never do.
public sealed class DiagnosticLoggerProvider(Diagnostics diagnostics) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new DiagnosticLogger(diagnostics);
    public void Dispose() { }
    private sealed class DiagnosticLogger(Diagnostics diagnostics) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && logLevel != LogLevel.None;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel) && Enum.IsDefined(typeof(DiagnosticEvent), eventId.Id)) diagnostics.Record((DiagnosticEvent)eventId.Id);
        }
    }
}
