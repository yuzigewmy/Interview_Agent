using System.IO;
using System.Net.Http;
using InterviewAgent.Core.Abstractions;
using InterviewAgent.Core.Settings;
using InterviewAgent.Infrastructure.Audio;
using InterviewAgent.Infrastructure.DeepSeek;
using InterviewAgent.Infrastructure.Documents;
using InterviewAgent.Infrastructure.Embeddings;
using InterviewAgent.Infrastructure.Knowledge;
using InterviewAgent.Infrastructure.Observability;
using InterviewAgent.Infrastructure.Security;
using InterviewAgent.Infrastructure.Volcano;

namespace InterviewAgent.App;

public sealed class AppRuntime : IAsyncDisposable
{
    private readonly HttpClient _httpClient;

    public AppRuntime()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        RootDirectory = Path.Combine(localAppData, "InterviewAgent");
        Directory.CreateDirectory(RootDirectory);
        var dataDirectory = Path.Combine(RootDirectory, "data");
        var knowledgeDatabasePath = Path.Combine(dataDirectory, "knowledge.db");
        KnowledgeDatabaseExistedAtStartup = File.Exists(knowledgeDatabasePath);

        SettingsStore = new DpapiSettingsStore(
            Path.Combine(RootDirectory, "secrets.dat"));
        KnowledgeRepository = new SqliteKnowledgeRepository(
            knowledgeDatabasePath,
            new DocxParagraphReader(),
            new LocalHashEmbeddingProvider(384));
        AudioCapture = new WasapiAudioCaptureService();
        Telemetry = new JsonlAppTelemetry(Path.Combine(RootDirectory, "logs"));
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(35),
        };
    }

    public string RootDirectory { get; }

    public bool KnowledgeDatabaseExistedAtStartup { get; }

    public ISettingsStore SettingsStore { get; }

    public IKnowledgeRepository KnowledgeRepository { get; }

    public IAudioCaptureService AudioCapture { get; }

    public IAppTelemetry Telemetry { get; }

    public IAnswerGenerator CreateAnswerGenerator(Func<AppSettings> settingsAccessor)
    {
        var settings = settingsAccessor();
        if (settings.DemoMode || !settings.HasDeepSeekCredential)
        {
            return new DemoAnswerGenerator();
        }

        return new DeepSeekAnswerGenerator(
            _httpClient,
            () =>
            {
                var current = settingsAccessor();
                return new DeepSeekOptions(current.DeepSeekApiKey);
            });
    }

    public IStreamingTranscriber CreateTranscriber(Func<AppSettings> settingsAccessor)
    {
        var settings = settingsAccessor();
        if (settings.DemoMode || !settings.HasVolcanoCredential)
        {
            return new DemoStreamingTranscriber();
        }

        return new VolcanoStreamingTranscriber(
            () =>
            {
                var current = settingsAccessor();
                return new VolcanoAsrOptions(
                    current.VolcanoApiKey,
                    current.VolcanoAppKey,
                    current.VolcanoAccessKey,
                    current.VolcanoResourceId);
            });
    }

    public async ValueTask DisposeAsync()
    {
        AudioCapture.Dispose();
        if (Telemetry is IDisposable disposableTelemetry)
        {
            disposableTelemetry.Dispose();
        }

        _httpClient.Dispose();
        await ValueTask.CompletedTask;
    }
}
