# SerilogTracing [![NuGet Version](https://img.shields.io/nuget/vpre/SerilogTracing.svg?style=flat)](https://www.nuget.org/packages/SerilogTracing/)

SerilogTracing is a minimal tracing system that integrates Serilog with .NET's `System.Diagnostics.Activity`. You can use it to add distributed, hierarchical tracing to applications that use Serilog, and to consume traces generated by .NET components including `HttpClient` and ASP.NET Core.

Traces are written to standard Serilog sinks. Most sinks will currently flatten traces into individual spans, but it's easy to add full tracing support to sinks with capable back-ends, and the project ships tracing-enabled sinks for OpenTelemetry, Seq, and Zipkin.

Here's the output of the included [example application](https://github.com/serilog-tracing/serilog-tracing/tree/dev/example) in the standard `System.Console` sink:

![SerilogTracing terminal output](https://raw.githubusercontent.com/nblumhardt/serilog-tracing/dev/assets/terminal-output.png)

The same trace displayed in Seq:

![SerilogTracing Seq output](https://raw.githubusercontent.com/nblumhardt/serilog-tracing/dev/assets/seq-output.png)

And in Zipkin:

![SerilogTracing Zipkin output](https://raw.githubusercontent.com/nblumhardt/serilog-tracing/dev/assets/zipkin-output.png)

## Getting started

This section walks through a very simple SerilogTracing example. To get started we'll create a simple .NET 8 console application and install some SerilogTracing packages.

```sh
mkdir example
cd example
dotnet new console
dotnet add package SerilogTracing --prerelease
dotnet add package SerilogTracing.Expressions --prerelease
dotnet add package Serilog.Sinks.Console
```

Replace the contents of the generated `Program.cs` with:

```csharp
using Serilog;
using SerilogTracing.Expressions;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(Formatters.CreateConsoleTextFormatter())
    .CreateLogger();

using var _ = new TracingConfiguration().EnableTracing();

using var activity = Log.Logger.StartActivity("Check {Host}", "example.com");
try
{
    var client = new HttpClient();
    var content = client.GetStringAsync("https://example.com");
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
using SerilogTracing.Expressions;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(Formatters.CreateConsoleTextFormatter())
    .CreateLogger();
```

The `Formatters.CreateConsoleTextFormatter()` function comes from `SerilogTracing.Expressions`; you can ignore this and use a regular console output template, but the one we're using here produces nice output for spans that includes timing information. Dig into the implementation of the `CreateConsoleTextFormatter()` function if you'd like to see how to set up your own trace-specific formatting, it's pretty straightforward.

### Enabling tracing with `TracingConfiguration.EnableTracing()`

This line sets up SerilogTracing's integration with .NET's diagnostic sources, and starts an activity listener in the background that will write spans from the framework and third-party libraries through your Serilog pipeline:

```csharp
using var _ = new TracingConfiguration().EnableTracing();
```

This step is optional, but you'll need this if you want to view your SerilogTracing output as hierarchical, distributed traces: without it, `HttpClient` won't generate spans, and won't propagate trace ids along with outbound HTTP requests.

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

* [`SerilogTracing.Sinks.OpenTelemetry`](https://www.nuget.org/packages/SerilogTracing.Sinks.OpenTelemetry/) &mdash; call `WriteTo.OpenTelemetry()` and pass `tracingEndpoint` along with `logsEndpoint` to send traces and logs using OTLP.
* [`SerilogTracing.Sinks.Seq`](https://www.nuget.org/packages/SerilogTracing.Sinks.Seq/) - call `WriteTo.SeqTracing()` to send logs and traces to Seq; use `Enrich.WithProperty("Application", "your app")` to show service names in traces.
* [`SerilogTracing.Sinks.Zipkin`](https://www.nuget.org/packages/SerilogTracing.Sinks.Zipkin/) - call `WriteTo.Zipkin` to send traces to Zipkin; logs are ignored by this sink.

## Adding instrumentation for ASP.NET Core

If you're writing an ASP.NET Core application, you'll notice that the spans generated in response to web requests have very generic names, like `HttpRequestIn`. To fix that, first add `SerilogTracing.Instrumentation.AspNetCore`:

```sh
dotnet add package SerilogTracing.Instrumentation.AspNetCore
```

Then add `Instrument.AspNetCore()` to your `TracingConfiguration`:

```csharp
using var _ = new TracingConfiguration()
    .Instrument.AspNetCoreRequests()
    .EnableTracing();
```

## How are traces represented as `LogEvent`s?

Traces are collections of spans, connected by a common trace id. SerilogTracing maps the typical properties associated with a span onto Serilog `LogEvent` instances:

| Span feature | `LogEvent` property |
| --- | --- |
| Trace id | `TraceId` |
| Span id | `SpanId` |
| Parent id | `Properties["ParentSpanId"]` |
| Name | `MessageTemplate` |
| Start | `Properties["SpanStartTimestamp"]` |
| End | `Timestamp` |
| Status | `Level` |
| Status description or error event | `Exception` |
| Tags | `Properties[*]` |

## What's the relationship between SerilogTracing and OpenTelemetry?

OpenTelemetry is a project that combines a variety of telemetry data models, schemas, APIs, and SDKs. SerilogTracing, like Serilog itself, has no dependency on the OpenTelemetry SDK, but can produce OpenTelemetry-compatible data using the OpenTelemetry Protocol (OTLP). This is considered to be on equal footing with the many other protocols and systems that exist in the wider Serilog ecosystem.

If you're working in an environment with deep investment in OpenTelemetry, you might consider using the [OpenTelemetry .NET SDK](https://opentelemetry.io/docs/languages/net/) instead of SerilogTracing. If you're seeking lightweight, deliberate instrumentation that has the same crafted feel and tight control offered by Serilog, you're in the right place.

### `SerilogTracing.Sinks.OpenTelemetry`

SerilogTracing includes a fork of [_Serilog.Sinks.OpenTelemetry_](https://github.com/serilog/serilog-sinks-opentelemetry). This is necessary (for now) because _Serilog.Sinks.OpenTelemetry_ only supports the OTLP logs protocol: _SerilogTracing.Sinks.OpenTelemetry_ extends this with support for OTLP traces.

## Who is developing SerilogTracing?

SerilogTracing is an open source (Apache 2.0) project that welcomes your ideas and contributions. It's built by @nblumhardt (also a Serilog maintainer), @liammclennan and @kodraus from Datalust, the company behind Seq.

SerilogTracing is not an official Serilog or Datalust project, but our hope for it is that it can serve as a validation and a basis for deeper tracing support in Serilog in the future.
