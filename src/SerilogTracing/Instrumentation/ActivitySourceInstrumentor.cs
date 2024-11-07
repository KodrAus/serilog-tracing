using System.Diagnostics;
using SerilogTracing.Core;

namespace SerilogTracing.Instrumentation;

/// <summary>
/// An instrumentor that observes events when activities are started or stopped.
/// </summary>
public abstract class ActivitySourceInstrumentor : IActivityInstrumentor
{
    /// <inheritdoc />
    public bool ShouldSubscribeTo(string diagnosticListenerName)
    {
        return diagnosticListenerName == Constants.SerilogTracingActivitySourceName;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="activity"></param>
    protected abstract void InstrumentActivity(Activity activity);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="source"></param>
    /// <returns></returns>
    protected abstract bool ShouldInstrument(ActivitySource source);
    
    /// <inheritdoc />
    void IActivityInstrumentor.InstrumentActivity(Activity activity, string eventName, object eventArgs)
    {
        if (!ShouldInstrument(activity.Source))
            return;
        
        switch (eventName)
        {
            case Constants.SerilogTracingActivityStartedEventName:
                InstrumentActivity(activity);
                return;
        }
    }
}
