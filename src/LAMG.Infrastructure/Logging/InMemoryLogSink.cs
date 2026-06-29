using LAMG.Application.Abstractions.System;
using LAMG.Common;
using LAMG.Infrastructure.Configuration;

using Microsoft.Extensions.Options;

using Serilog.Core;
using Serilog.Events;

namespace LAMG.Infrastructure.Logging;

/// <summary>
/// Serilog sink backed by a fixed-size ring buffer. The Processing
/// screen reads <see cref="Snapshot"/> on load and subscribes to
/// <see cref="LineWritten"/> for live updates. Older entries are
/// evicted; the rolling file sink retains the full history.
/// </summary>
public sealed class InMemoryLogSink : ILogEventSink, ILogReader
{
    private readonly object _lock = new();
    private readonly Queue<LogLine> _buffer;
    private readonly int _capacity;

    public InMemoryLogSink(IOptions<InfrastructureOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        InfrastructureOptions opts = Guard.NotNull(options.Value);
        _capacity = opts.InMemoryLogCapacity > 0 ? opts.InMemoryLogCapacity : 5000;
        _buffer = new Queue<LogLine>(capacity: _capacity);
    }

    public event EventHandler<LogLine>? LineWritten;

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        LogLine line = new(
            Timestamp: logEvent.Timestamp,
            Level: logEvent.Level.ToString(),
            Source: ExtractSource(logEvent),
            Message: logEvent.RenderMessage(),
            Exception: logEvent.Exception?.ToString());

        lock (_lock)
        {
            if (_buffer.Count >= _capacity)
            {
                _buffer.Dequeue();
            }

            _buffer.Enqueue(line);
        }

        // Fire outside the lock to avoid recursive deadlock from a
        // synchronous subscriber that logs in turn.
        try
        {
            LineWritten?.Invoke(this, line);
        }
        catch
        {
            // Subscribers must never crash the logger.
        }
    }

    public IReadOnlyList<LogLine> Snapshot()
    {
        lock (_lock)
        {
            return _buffer.ToArray();
        }
    }

    private static string ExtractSource(LogEvent logEvent)
    {
        if (logEvent.Properties.TryGetValue("SourceContext", out LogEventPropertyValue? value)
            && value is ScalarValue scalar
            && scalar.Value is string s)
        {
            return s;
        }

        return string.Empty;
    }
}
