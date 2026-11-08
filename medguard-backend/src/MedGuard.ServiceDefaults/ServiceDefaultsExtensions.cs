using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace MedGuard.ServiceDefaults;

public class ObservabilityOptions
{
    /// <summary>Off by default, so the app runs with no observability stack. The dev
    /// launch profiles turn it on; then <c>docker compose --profile observability up -d</c>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Seq receives logs (Serilog) and traces (OTLP/HTTP).</summary>
    public string SeqUrl { get; set; } = "http://localhost:5341";

    /// <summary>Prometheus' native OTLP receiver (needs --web.enable-otlp-receiver).</summary>
    public string PrometheusOtlpUrl { get; set; } = "http://localhost:9090/api/v1/otlp/v1/metrics";
}

public static class ServiceDefaultsExtensions
{
    /// <summary>Health checks tagged "live" answer "is the process stuck?" (restart it);
    /// "ready" answers "can it do its job right now?" (stop sending it work).</summary>
    public const string Live = "live", Ready = "ready";

    /// <summary>
    /// Logging, tracing, metrics and health checks, set up the same way in every
    /// service (ADR 0011). Call it first in Program.cs.
    /// </summary>
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder, string serviceName, string? instanceId = null)
    {
        var obs = builder.Configuration.GetSection("Observability").Get<ObservabilityOptions>() ?? new();
        instanceId ??= Environment.MachineName;

        // --- Logs: Serilog -> console, + Seq when enabled. Serilog stamps every
        // event with the current TraceId/SpanId, so in Seq a log line links
        // straight to the trace it happened in.
        builder.Services.AddSerilog(lc =>
        {
            lc.MinimumLevel.Information()
              .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
              .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
              .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
              .ReadFrom.Configuration(builder.Configuration) // optional "Serilog" section overrides the above
              .Enrich.FromLogContext()
              .Enrich.WithProperty("service", serviceName)
              .Enrich.WithProperty("instance", instanceId)
              .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} {Level:u4} {SourceContext}: {Message:lj}{NewLine}{Exception}");
            if (obs.Enabled)
                lc.WriteTo.Seq(obs.SeqUrl);
        });

        builder.Services.AddHealthChecks();
        // A probe that doesn't answer is as bad as one that says "down": orchestrators
        // give up after a few seconds. Any check without its own timeout gets 5s.
        // (Measured: EF's CanConnect retries a failed login for 60s, ADR 0010.)
        builder.Services.PostConfigure<HealthCheckServiceOptions>(o =>
        {
            foreach (var registration in o.Registrations.Where(r => r.Timeout == Timeout.InfiniteTimeSpan))
                registration.Timeout = TimeSpan.FromSeconds(5);
        });

        if (!obs.Enabled)
            return builder;

        // --- Traces + metrics: OpenTelemetry.
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceInstanceId: instanceId))
            .WithTracing(t => t
                .AddSource("MedGuard.*")
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health"))
                .AddSqlClientInstrumentation()
                .AddOtlpExporter(o =>
                {
                    o.Endpoint = new Uri($"{obs.SeqUrl.TrimEnd('/')}/ingest/otlp/v1/traces");
                    o.Protocol = OtlpExportProtocol.HttpProtobuf;
                }))
            .WithMetrics(m => m
                .AddMeter("MedGuard.*")
                .AddAspNetCoreInstrumentation()
                // Latency buckets that fit this pipeline (ms): sub-100ms healthy,
                // seconds = backlog. The OTel default buckets would lump it all together.
                .AddView("medguard.readings.e2e_latency", new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = [10, 25, 50, 100, 250, 500, 1_000, 2_500, 5_000, 10_000, 30_000, 60_000],
                })
                .AddView("medguard.worker.db_duration", new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = [1, 2, 5, 10, 25, 50, 100, 250, 500, 1_000],
                })
                .AddOtlpExporter((o, reader) =>
                {
                    o.Endpoint = new Uri(obs.PrometheusOtlpUrl);
                    o.Protocol = OtlpExportProtocol.HttpProtobuf;
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 5_000;
                }));

        return builder;
    }

    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = c => c.Tags.Contains(Live), ResponseWriter = WriteJson });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(Ready), ResponseWriter = WriteJson });
        return app;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static Task WriteJson(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            durationMs = (int)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description ?? e.Value.Exception?.Message,
                data = e.Value.Data.Count > 0 ? e.Value.Data : null,
            }),
        }, Json));
    }
}
