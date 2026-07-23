using System.Text.Json;
using InterviewAgent.Core.Abstractions;
using InterviewAgent.Core.Telemetry;

namespace InterviewAgent.Infrastructure.Observability;

public sealed class JsonlAppTelemetry : IAppTelemetry, IDisposable
{
    private static readonly HashSet<string> AllowedFields =
        new(StringComparer.Ordinal)
        {
            "mode",
            "provider",
            "stage",
            "durationMs",
            "hitCount",
            "usedFallback",
            "errorCode",
            "chunkCount",
            "model",
        };
    private readonly string _directory;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public JsonlAppTelemetry(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    public async ValueTask TrackAsync(
        AppTelemetryEvent telemetryEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);
        if (string.IsNullOrWhiteSpace(telemetryEvent.EventName) ||
            string.IsNullOrWhiteSpace(telemetryEvent.OperationId))
        {
            throw new ArgumentException("遥测事件名和关联 ID 不能为空。");
        }

        if (telemetryEvent.Fields.Keys.Any(key => !AllowedFields.Contains(key)))
        {
            throw new ArgumentException("遥测事件包含未获准字段。");
        }

        Directory.CreateDirectory(_directory);
        var line = JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UtcNow,
            eventName = telemetryEvent.EventName,
            operationId = telemetryEvent.OperationId,
            fields = telemetryEvent.Fields,
        });
        var path = Path.Combine(
            _directory,
            $"interview-agent-{DateTime.UtcNow:yyyyMMdd}.jsonl");

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(
                path,
                line + Environment.NewLine,
                cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Dispose() => _writeGate.Dispose();
}

