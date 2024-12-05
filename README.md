# SerilogTracing [![NuGet Version](https://img.shields.io/nuget/v/SerilogTracing.svg?style=flat)](https://www.nuget.org/packages/SerilogTracing/)

SerilogTracing is a minimal tracing system that integrates Serilog with .NET's `System.Diagnostics.Activity`. You can use it to add distributed, hierarchical tracing to applications that use Serilog, and to consume traces generated by .NET components including `HttpClient` and ASP.NET Core.

Traces are written to standard Serilog sinks. [Sinks with capable back-ends](#tracing-enabled-sinks) support full hierarchical tracing, and others will flatten traces into individual spans with timing information.

Here's the output of the included [example application](https://github.com/serilog-tracing/serilog-tracing/tree/dev/example) in the standard `System.Console` sink:

![SerilogTracing terminal output](https://raw.githubusercontent.com/serilog-tracing/serilog-tracing/dev/assets/terminal-output.png)

The same trace displayed in Seq:

![SerilogTracing Seq output](https://raw.githubusercontent.com/serilog-tracing/serilog-tracing/dev/assets/seq-output.png)

And in Zipkin:

![SerilogTracing Zipkin output](https://raw.githubusercontent.com/serilog-tracing/serilog-tracing/dev/assets/zipkin-output.png)

## Getting started

This section walks through a very simple SerilogTracing example. To get started we'll create a simple .NET 8 console application and install some SerilogTracing packages.

```sh
mkdir example
cd example
dotnet new console
dotnet add package SerilogTracing
dotnet add package SerilogTracing.Expressions
dotnet add package Serilog.Sinks.Console
```

Replace the contents of the generated `Program.cs` with:

```csharp
using Serilog;
using Serilog.Templates.Themes;
using SerilogTracing;
using SerilogTracing.Expressions;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(Formatters.CreateConsoleTextFormatter(TemplateTheme.Code))
    .CreateLogger();

using var listener = new ActivityListenerConfiguration().TraceToSharedLogger();

using var activity = Log.Logger.StartActivity("Check {Host}", "example.com");
try
{
    var client = new HttpClient();
    var content = await client.GetStringAsync("https://example.com");
    Log.Information("Content length is {ContentLength}", content.Length);

    activity.Complete();
}
catch (Exception ex)
{
    activity.Complete(LogEventLevel.Fatal, ex);
}
finally
{
    await Log.CloseAndFlushAsync();
}
```

Running it will print some log events and spans to the console:

```sh
dotnet run
```

Let's break the example down a bit.

### Setting up the logger

The Serilog pipeline is set up normally:

```csharp
using Serilog;
using Serilog.Templates.Themes;
using SerilogTracing;
using SerilogTracing.Expressions;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(Formatters.CreateConsoleTextFormatter(TemplateTheme.Code))
    .CreateLogger();
```

The `Formatters.CreateConsoleTextFormatter()` function comes from `SerilogTracing.Expressions`; you can ignore this and use a regular console output template, but the one we're using here produces nice output for spans that includes timing information. Dig into the implementation of the `CreateConsoleTextFormatter()` function if you'd like to see how to set up your own trace-specific formatting, it's pretty straightforward.

### Enabling tracing with `ActivityListenerConfiguration.TraceToSharedLogger()`

This line sets up SerilogTracing's integration with .NET's diagnostic sources, and starts an activity listener in the background that will write spans from the framework and third-party libraries through your Serilog pipeline:

```csharp
using var listener = new ActivityListenerConfiguration().TraceToSharedLogger();
```

This step is optional, but you'll need this if you want to view your SerilogTracing output as hierarchical, distributed traces: without it, `HttpClient` won't generate spans, and won't propagate trace ids along with outbound HTTP requests.

You can also configure SerilogTracing to send spans through a specific `ILogger`:

```csharp
using Serilog;
using SerilogTracing;
using SerilogTracing.Expressions;

await using var logger = new LoggerConfiguration()
    .WriteTo.Console(Formatters.CreateConsoleTextFormatter())
    .CreateLogger();

using var listener = new ActivityListenerConfiguration().TraceTo(logger);
```

### Starting and completing activities

`ILogger.StartActivity()` is the main SerilogTracing API for starting activities. It works on any `ILogger`, and the span generated by the activity will be written through that logger, receiving the same enrichment and filtering as any other log event.

```csharp
using var activity = Log.Logger.StartActivity("Check {Host}", "example.com");
```

`StartActivity` accepts a [message template](https://messagetemplates.org), just like Serilog, and you can capture structured properties by including them in the template.

The object returned from `StartActivity()` is a `LoggerActivity`, to which you can add additional structured data using `AddProperty()`.

The `LoggerActivity` implements `IDisposable`, and if you let the activity be disposed normally, it will record the activity as complete, and write a span through the underlying `ILogger`.

In the example, because the activity needs to be completed before the `Log.CloseAndFlushAsync()` call at the end, we call `Complete()` explicitly on the success path:

```csharp
try
{
    // ...
    activity.Complete();
}
catch (Exception ex)
{
    activity.Complete(LogEventLevel.Fatal, ex);
}
```

On the failure path, we call the overload of `Complete()` that accepts a level and exception, to mark the activity as failed and use the specified level for the generated log event.

## Tracing-enabled sinks

These sinks have been built or modified to work well with tracing back-ends:

* [`Serilog.Sinks.Seq`](https://www.nuget.org/packages/Serilog.Sinks.Seq/) - call `WriteTo.Seq()` to send logs and traces to Seq; use `Enrich.WithProperty("Application", "your app")` to show service names in traces.
* [`Serilog.Sinks.OpenTelemetry`](https://www.nuget.org/packages/Serilog.Sinks.OpenTelemetry/) &mdash; call `WriteTo.OpenTelemetry()` to send traces and logs using OTLP.
* [`SerilogTracing.Sinks.Zipkin`](https://www.nuget.org/packages/SerilogTracing.Sinks.Zipkin/) - call `WriteTo.Zipkin()` to send traces to Zipkin; logs are ignored by this sink.

To add tracing support to an existing sink, see [how activities are mapped onto `LogEvent`s](#mapping-trace-concepts-to-event-properties).

## Adding instrumentation for ASP.NET Core requests

If you're writing an ASP.NET Core application, you'll notice that the spans generated in response to web requests have very generic names, like `HttpRequestIn`. To fix that, first add `SerilogTracing.Instrumentation.AspNetCore`:

```sh
dotnet add package SerilogTracing.Instrumentation.AspNetCore --prerelease
```

Then add `Instrument.AspNetCoreRequests()` to your `ActivityListenerConfiguration`:

```csharp
using var listener = new ActivityListenerConfiguration()
    .Instrument.AspNetCoreRequests()
    .TraceToSharedLogger();
```

### Incoming `traceparent` headers

HTTP requests received by ASP.NET Core may contain a header with the trace id, span id, and sampling decision made for the active span in the calling application. How this header is used can be configured with `HttpRequestInActivityInstrumentationOptions.IncomingTraceParent`:

```csharp
using var listener = new ActivityListenerConfiguration()
    .Instrument.AspNetCoreRequests(opts =>
    {
        opts.IncomingTraceParent = IncomingTraceParent.Trust;
    })
    .TraceToSharedLogger();
```

The supported options are:

 * **`IncomingTraceParent.Accept`** (default) &mdash; the parent's trace and span ids will be used, but the sampling decision will be ignored; this reveals the presence of incoming tracing information while preventing callers from controlling whether data is recorded
 * **`IncomingTraceParent.Ignore`** &mdash; no information about the parent span will be preserved; this is the appropriate option for most public or Internet-facing sites and services
 * **`IncomingTraceParent.Trust`** &mdash; use the parent's trace and span ids, and respect the parent's sampling decision; this is the appropriate option for many internal services, since it allows system-wide sampling and consistent, detailed traces

See the section [Sampling](#sampling) below for more information on how sampling works in SerilogTracing.

## Adding instrumentation for `HttpClient` requests

`HttpClient` requests are instrumented by default. To configure the way `HttpClient` requests are recorded as spans, remove the default instrumentation and add `HttpClient` instrumentation explicitly:

```csharp
using var listener = new ActivityListenerConfiguration()
    .Instrument.WithDefaultInstrumentation(false)
    .Instrument.HttpClientRequests(opts => opts.MessageTemplate = "Hello, world!")
    .TraceToSharedLogger();
```

The message template for spans, and mappings from `HttpRequestMessage` and `HttpResponseMessage` into log event properties and the completion level can be configured. 

## Adding instrumentation for `Microsoft.Data.SqlClient` commands

Microsoft's client library for SQL Server doesn't generate spans by default. To turn on tracing of database commands, install `SerilogTracing.Instrumentation.SqlClient`:

```sh
dotnet add package SerilogTracing.Instrumentation.SqlClient --prerelease
```

Then add `Instrument.SqlClientCommands()` to your `ActivityListenerConfiguration`:

```csharp
using var listener = new ActivityListenerConfiguration()
    .Instrument.SqlClientCommands()
    .TraceToSharedLogger();
```

## Adding instrumentation for `Npgsql` commands

Npgsql is internally instrumented using `System.Diagnostics.Activity`, so no additional packages or steps are required to enable instrumentation of Npgsql commands. If you're missing spans from Npgsql, check that the `"Npgsql"` namespace isn't suppressed by your `MinimumLevel.Override()` configuration.

## Formatting output

SerilogTracing includes extensions to [_Serilog.Expressions_](https://github.com/serilog/serilog-expressions) aimed at producing useful text and JSON output from
spans:

```
dotnet add package SerilogTracing.Expressions --prerelease
```

For console output, `Formatters.CreateConsoleTextFormatter()` provides span timings in a pleasant ANSI-colored format:

```csharp
Log.Logger = new LoggerConfiguration()
    // The `Formatters` class is from `SerilogTracing.Expressions`
    .WriteTo.Console(Formatters.CreateConsoleTextFormatter(TemplateTheme.Code))
    .CreateLogger();
```

Alternatively, `TracingNameResolver` can be used with `ExpressionTemplate` to create text or JSON output. The
example above expands into the (admittedly quite dense) template below:

```csharp
var formatter = new ExpressionTemplate(
    "[{@t:HH:mm:ss} {@l:u3}] " +
    "{#if IsRootSpan()}\u2514\u2500 {#else if IsSpan()}\u251c {#else if @sp is not null}\u2502 {#else}\u250A {#end}" +
    "{@m}" +
    "{#if IsSpan()} ({Milliseconds(Elapsed()):0.###} ms){#end}" +
    "\n" +
    "{@x}",
    theme: TemplateTheme.Code,
    nameResolver: new TracingNameResolver());

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatter)
    .CreateLogger();
```

For an example showing how to produce JSON with `ExpressionTemplate`, see the implementation of `ZipkinSink` in this repository,
and [this article introducing _Serilog.Expressions_ JSON support](https://nblumhardt.com/2021/06/customize-serilog-json-output/).

## Sampling

Sampling is a method of reducing stored data volumes by selectively recording traces. This is similar to levelling, but instead of turning individual span types on and off, sampling causes either _all_ of the spans in a trace to be recorded, or _none_ of them.

SerilogTracing implements two simple strategies via `ActivityListenerConfiguration`: `Sample.AllTraces()`, which records all traces (the default), and `Sample.OneTraceIn()`, which records a fixed proportion of possible traces:

```csharp
// Record only every 1000th trace
using var listener = new ActivityListenerConfiguration()
    .Sample.OneTraceIn(1000)
    .TraceToSharedLogger();
```

More sophisticated sampling strategies can be plugged in through `Sample.Using()`. These behave like the raw `System.Diagnostics.ActivityListener` API, but only apply to root spans. Setting the `ignoreParent` method parameter to `true` can be used to exactly mimic the `System.Diagnostics.ActivityListener` behavior.

> [!NOTE]
> Once a sampling decision has been made for the root activity in a trace, SerilogTracing's sampling infrastructure will ensure all child activities inherit that sampling decision, regardless of the sampling policy in use. This means that when sampling decisions are communicated by a remote caller, care should be taken to either discard or trust that caller's decision. See the section [Adding instrumentation for ASP.NET Core requests](#adding-instrumentation-for-aspnet-core-requests) for information on how to do this with SerilogTracing's ASP.NET Core integration.

Sampling does not affect the recording of log events: log events written during an un-sampled trace will still be recorded, and will carry trace and span ids even though the corresponding spans will be missing.

## How an `Activity` becomes a `LogEvent`

![SerilogTracing pipeline](https://raw.githubusercontent.com/serilog-tracing/serilog-tracing/dev/assets/pipeline-architecture.png)

Applications using SerilogTracing add tracing using `ILogger.StartActivity()`. These activities are always converted into `LogEvent`s and emitted through the original `ILogger` that created them.
.NET libraries and frameworks add tracing using `System.Diagnostics.ActivitySource`s. These activities are also emitted as `LogEvent`s when using `SerilogTracing.ActivityListenerConfiguration`.

### Mapping trace concepts to event properties

Traces are collections of spans, connected by a common trace id. SerilogTracing maps the typical properties associated with a span onto Serilog `LogEvent` instances:

| Span feature                      | `LogEvent` property                |
|-----------------------------------|------------------------------------|
| Trace id                          | `TraceId`                          |
| Span id                           | `SpanId`                           |
| Parent id                         | `Properties["ParentSpanId"]`       |
| Kind                              | `Properties["SpanKind"]`           |
| Name                              | `MessageTemplate`                  |
| Start                             | `Properties["SpanStartTimestamp"]` |
| End                               | `Timestamp`                        |
| Status                            | `Level`                            |
| Status description or error event | `Exception`                        |
| Tags                              | `Properties[*]`                    |

### Levelling for external activity sources

SerilogTracing can consume activities from .NET itself, and libraries that don't themselves use SerilogTracing. By default, you'll see spans for all activities, from all sources, in your Serilog output.

To "turn down" the level of tracing performed by an external activity source, use SerilogTracing's `InitialLevel` configuration to set a level for spans from that source:

```csharp
    .InitialLevel.Override("Npgsql", LogEventLevel.Debug)
```

In this example, when activities from the [Npgsql](https://github.com/npgsql/npgsql) activity source are assigned an initial level of `Debug`, they'll be suppressed unless your Serilog logger has debug logging enabled.

#### Why is this an _initial_ level?

The initial level assigned to a source determines whether activities are created by the source. When the activity is completed, it may be recorded at a higher level; for example, a span created at an initial `Information` level may complete as an `Error` (but not at a lower level such as `Debug`, because doing so may suppress the span cause the trace hierarchy to become incoherent).

### Recording `Activity.Events`

Activities produced by external .NET libraries may include one or more embedded `ActivityEvent`s. By default, SerilogTracing
ignores these, except in the case of `exception` events, which map to the `LogEvent.Exception` property.

To emit additional `LogEvent`s for each embedded `ActivityEvent`, call `ActivityEvents.AsLogEvents()` on `ActivityListenerConfiguration`.

## What's the relationship between SerilogTracing and OpenTelemetry?

OpenTelemetry is a project that combines a variety of telemetry data models, schemas, APIs, and SDKs. SerilogTracing, like Serilog itself, has no dependency on the OpenTelemetry SDK, but can output traces using the OpenTelemetry Protocol (OTLP). From the point of view of SerilogTracing, this is considered to be just one of many protocols and systems that exist in the wider Serilog ecosystem.

If you're working in an environment with deep investment in OpenTelemetry, you might consider using the [OpenTelemetry .NET SDK](https://opentelemetry.io/docs/languages/net/) instead of SerilogTracing. If you're seeking lightweight, deliberate instrumentation that has the same crafted feel and tight control offered by Serilog, you're in the right place.

## Who is developing SerilogTracing?

SerilogTracing is an open source (Apache 2.0) project that welcomes your ideas and contributions. It's built by @nblumhardt (also a Serilog maintainer), @liammclennan and @kodraus from Datalust, the company behind Seq.

SerilogTracing is not an official Serilog or Datalust project, but our hope for it is that it can serve as a validation and a basis for deeper tracing support in Serilog in the future.
