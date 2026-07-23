using InterviewAgent.Core.Telemetry;

namespace InterviewAgent.Core.Abstractions;

public interface IAppTelemetry
{
    ValueTask TrackAsync(
        AppTelemetryEvent telemetryEvent,
        CancellationToken cancellationToken);
}

