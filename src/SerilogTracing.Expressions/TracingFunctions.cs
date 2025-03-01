using System.Diagnostics;
using Serilog.Events;
using SerilogTracing.Core;

// Plug-in functions have a standard signature with nullable return type.
// ReSharper disable ReturnTypeCanBeNotNullable

#if NETSTANDARD2_0
using SerilogTracing.Expressions.Pollyfill;
#else
using System.Diagnostics.CodeAnalysis;
#endif

namespace SerilogTracing.Expressions;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
static class TracingFunctions
{
    static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    
    public static LogEventPropertyValue? Elapsed(LogEvent logEvent)
    {
        if (logEvent.Properties.TryGetValue(Constants.SpanStartTimestampPropertyName, out var sst) &&
            sst is ScalarValue { Value: DateTime spanStart })
        {
            return new ScalarValue(logEvent.Timestamp - spanStart);
        }

        return null;
    }

    public static LogEventPropertyValue? IsSpan(LogEvent logEvent)
    {
        // As strict as possible.
        return new ScalarValue(logEvent is { TraceId: not null, SpanId: not null } &&
                               logEvent.Properties.TryGetValue(Constants.SpanStartTimestampPropertyName, out var sst) &&
                               sst is ScalarValue { Value: DateTime } &&
                               (!logEvent.Properties.TryGetValue(Constants.ParentSpanIdPropertyName, out var psi) ||
                                psi is ScalarValue { Value: ActivitySpanId }));
    }

    public static LogEventPropertyValue? IsRootSpan(LogEvent logEvent)
    {
        // As strict as possible.
        return new ScalarValue(logEvent is { TraceId: not null, SpanId: not null } &&
                               logEvent.Properties.TryGetValue(Constants.SpanStartTimestampPropertyName, out var sst) &&
                               sst is ScalarValue { Value: DateTime } &&
                               !logEvent.Properties.TryGetValue(Constants.ParentSpanIdPropertyName, out _));
    }

    public static LogEventPropertyValue? FromUnixEpoch(LogEventPropertyValue? dateTime)
    {
        return dateTime switch
        {
            ScalarValue { Value: DateTime dt } => new ScalarValue(dt.ToUniversalTime() - UnixEpoch),
            ScalarValue { Value: DateTimeOffset dto } => new ScalarValue(dto.UtcDateTime - UnixEpoch),
            _ => null
        };
    }

    public static LogEventPropertyValue? Milliseconds(LogEventPropertyValue? timeSpan)
    {
        // Casts (truncates) rather than rounding.
        if (timeSpan is ScalarValue { Value: TimeSpan ts })
            return new ScalarValue((decimal)ts.Ticks / TimeSpan.TicksPerMillisecond);

        return null;
    }

    public static LogEventPropertyValue? Microseconds(LogEventPropertyValue? timeSpan)
    {
        // Casts (truncates) rather than rounding.
        if (timeSpan is ScalarValue { Value: TimeSpan ts })
            return new ScalarValue((decimal)ts.Ticks / 10);

        return null;
    }

    public static LogEventPropertyValue? Nanoseconds(LogEventPropertyValue? timeSpan)
    {
        if (timeSpan is not ScalarValue { Value: TimeSpan ts })
            return null;

        if (ts >= TimeSpan.Zero)
        {
            if ((ulong)ts.Ticks <= ulong.MaxValue / 100)
                return new ScalarValue((ulong)ts.Ticks * 100);
        }
        else
        {
            if (ts.Ticks >= long.MinValue / 100)
                return new ScalarValue(ts.Ticks * 100);
        }

        return null;
    }
}
