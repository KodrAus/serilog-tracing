﻿using System.Diagnostics;
using SerilogTracing.Core;

namespace SerilogTracing.Interop;

static class LoggerActivitySource
{
    static ActivitySource Instance { get; } = new(Constants.SerilogActivitySourceName, null);

    public static Activity? TryStartActivity(string name)
    {
        // `ActivityKind` might be passed through here in the future. The `Activity` constructor does
        // not accept this.
        
        if (Instance.HasListeners())
        {
            // Tracing is enabled; if this returns `null`, sampling is suppressing the activity and so therefore
            // should the logging layer.
            var listenerActivity = Instance.CreateActivity(name, ActivityKind.Internal);

            // If this is the root activity then mark it as recorded
            if (listenerActivity is { ParentId: null })
            {
                listenerActivity.ActivityTraceFlags |= ActivityTraceFlags.Recorded;
            }

            listenerActivity?.Start();

            return listenerActivity;
        }
        
        // Tracing is not enabled. Levels are everything, and the level check has already been performed by the
        // caller, so we're in business!

        var manualActivity = new Activity(name);
        if (Activity.Current is {} parent)
        {
            manualActivity.SetParentId(parent.TraceId, parent.SpanId, parent.ActivityTraceFlags);
        }
        else
        {
            manualActivity.ActivityTraceFlags |= ActivityTraceFlags.Recorded;
        }

        manualActivity.Start();

        return manualActivity;
    }
}
