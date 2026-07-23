namespace InterviewAgent.Core.Telemetry;

public sealed record AppTelemetryEvent(
    string EventName,
    string OperationId,
    IReadOnlyDictionary<string, object?> Fields);

