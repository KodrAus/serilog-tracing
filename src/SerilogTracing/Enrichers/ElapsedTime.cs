﻿using Serilog.Core;
using Serilog.Events;
using SerilogTracing.Interop;

namespace SerilogTracing.Enrichers;

/// <summary>
/// An enricher that adds the span duration as a <see cref="TimeSpan"/>.
/// </summary>
public class ElapsedTime: ILogEventEnricher
{
    /// <summary>
    /// Construct an enricher that will add the span duration with the given property name. 
    /// </summary>
    /// <param name="propertyName">The name of the property to add with the span duration.</param>
    public ElapsedTime(string propertyName)
    {
        _propertyName = propertyName;
    }
    
    readonly string _propertyName;
    
    /// <inheritdoc cref="ILogEventEnricher.Enrich"/>
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent.TryGetElapsed(out var elapsed))
        {
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(_propertyName, elapsed.Value));
        }
    }
}
