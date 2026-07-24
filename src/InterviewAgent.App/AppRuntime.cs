using System.IO;
using System.Net.Http;
using InterviewAgent.Core.Abstractions;
using InterviewAgent.Core.Corrections;
using InterviewAgent.Core.Settings;
using InterviewAgent.Core.Sessions;
using InterviewAgent.Infrastructure.Audio;
using InterviewAgent.Infrastructure.Corrections;
using InterviewAgent.Infrastructure.DeepSeek;
using InterviewAgent.Infrastructure.Documents;
using InterviewAgent.Infrastructure.Embeddings;
using InterviewAgent.Infrastructure.Generation;
using InterviewAgent.Infrastructure.Knowledge;
using InterviewAgent.Infrastructure.Observability;
using InterviewAgent.Infrastructure.Security;
using InterviewAgent.Infrastructure.Volcano;

namespace InterviewAgent.App;

public sealed class AppRuntime : IAsyncDisposable
{
    private readonly IEmbeddingProvider _embeddingProvider;
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
        _embeddingProvider = LocalEmbeddingProviderFactory.Create(
            ResolveBgeModelDirectory(RootDirectory));
        EmbeddingProviderName = _embeddingProvider.Name;
        KnowledgeRepository = new SqliteKnowledgeRepository(
            knowledgeDatabasePath,
            new DocxParagraphReader(),
            _embeddingProvider);
        var asrResourceDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "resources",
            "asr");
        QuestionCorrector = DomainQuestionCorrector.LoadOrEmpty(
            Path.Combine(asrResourceDirectory, "correct_words.json"),
            Path.Combine(asrResourceDirectory, "volcano_hotwords.txt"));
        InterviewSession = new InMemoryInterviewSession();
        AudioCapture = new WasapiAudioCaptureService();
        Telemetry = new JsonlAppTelemetry(Path.Combine(RootDirectory, "logs"));
        _httpClient = new HttpClient(
            new HttpClientHandler
            {
                AllowAutoRedirect = false,
            })
        {
            Timeout = TimeSpan.FromSeconds(35),
        };
    }

    public string RootDirectory { get; }

    public bool KnowledgeDatabaseExistedAtStartup { get; }

    public string EmbeddingProviderName { get; }

    public ISettingsStore SettingsStore { get; }

    public IKnowledgeRepository KnowledgeRepository { get; }

    public IDomainQuestionCorrector QuestionCorrector { get; }

    public IInterviewSession InterviewSession { get; }

    public IAudioCaptureService AudioCapture { get; }

    public IAppTelemetry Telemetry { get; }

    public IAnswerGenerator CreateAnswerGenerator(Func<AppSettings> settingsAccessor)
    {
        var settings = settingsAccessor();
        if (settings.DemoMode)
        {
            return new DemoAnswerGenerator();
        }

        return new OpenAiCompatibleAnswerGenerator(
            _httpClient,
            () =>
            {
                var current = settingsAccessor();
                return new OpenAiCompatibleOptions(
                    current.ModelProvider,
                    current.ModelName,
                    current.ModelBaseUrl,
                    current.ModelApiKey,
                    current.ModelMaxTokens);
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

        if (_embeddingProvider is IDisposable disposableEmbeddingProvider)
        {
            disposableEmbeddingProvider.Dispose();
        }

        _httpClient.Dispose();
        await ValueTask.CompletedTask;
    }

    private static string ResolveBgeModelDirectory(string rootDirectory)
    {
        var userModelDirectory = Path.Combine(
            rootDirectory,
            "models",
            "bge-small-zh-v1.5");
        if (HasBgeFiles(userModelDirectory))
        {
            return userModelDirectory;
        }

        var bundledModelDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "models",
            "bge-small-zh-v1.5");
        return HasBgeFiles(bundledModelDirectory)
            ? bundledModelDirectory
            : userModelDirectory;
    }

    private static bool HasBgeFiles(string directory) =>
        File.Exists(Path.Combine(directory, "model.onnx")) &&
        File.Exists(Path.Combine(directory, "vocab.txt"));
}
