using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using System.Windows;
using InterviewAgent.Core.Abstractions;
using InterviewAgent.Core.Audio;
using InterviewAgent.Core.Knowledge;
using InterviewAgent.Core.Orchestration;
using InterviewAgent.Core.Settings;
using InterviewAgent.Core.Telemetry;

namespace InterviewAgent.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly AppRuntime _runtime;
    private readonly AnswerCoordinator _coordinator;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private AppSettings _settings = AppSettings.Default;
    private AudioDevice? _selectedAudioDevice;
    private string _transcript = string.Empty;
    private string _answer = string.Empty;
    private string _statusText = "正在初始化…";
    private string _knowledgeStatus = "尚未载入";
    private string _timingText = "等待问题";
    private bool _isCapturing;
    private bool _isBusy;
    private bool _fallbackShown;
    private IStreamingTranscriber? _transcriber;
    private Channel<AudioFrame>? _audioChannel;
    private Task? _audioPump;

    public MainViewModel(AppRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _coordinator = new AnswerCoordinator(
            runtime.KnowledgeRepository,
            () => runtime.CreateAnswerGenerator(() => Settings));
    }

    public ObservableCollection<AudioDevice> AudioDevices { get; } = new();

    public ObservableCollection<KnowledgeHit> Evidence { get; } = new();

    public AppSettings Settings
    {
        get => _settings;
        private set
        {
            if (SetProperty(ref _settings, value))
            {
                RaisePropertyChanged(nameof(ModeLabel));
                RaisePropertyChanged(nameof(AsrLabel));
                RaisePropertyChanged(nameof(ModelLabel));
            }
        }
    }

    public AudioDevice? SelectedAudioDevice
    {
        get => _selectedAudioDevice;
        set => SetProperty(ref _selectedAudioDevice, value);
    }

    public string Transcript
    {
        get => _transcript;
        set => SetProperty(ref _transcript, value);
    }

    public string Answer
    {
        get => _answer;
        private set => SetProperty(ref _answer, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string KnowledgeStatus
    {
        get => _knowledgeStatus;
        private set => SetProperty(ref _knowledgeStatus, value);
    }

    public string TimingText
    {
        get => _timingText;
        private set => SetProperty(ref _timingText, value);
    }

    public bool IsCapturing
    {
        get => _isCapturing;
        private set
        {
            if (SetProperty(ref _isCapturing, value))
            {
                RaisePropertyChanged(nameof(CaptureButtonText));
                RaisePropertyChanged(nameof(CaptureHint));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaisePropertyChanged(nameof(IsNotBusy));
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public string CaptureButtonText =>
        IsCapturing ? "停止采集并回答" : "开始采集问题";

    public string CaptureHint =>
        IsCapturing
            ? "正在采集 · 再按 F8 停止并生成回答"
            : "全局快捷键 F8 · 仅在你主动开启时采集";

    public string ModeLabel => Settings.DemoMode ? "演示模式" : "云服务模式";

    public string AsrLabel =>
        Settings.DemoMode
            ? "ASR：演示"
            : Settings.HasVolcanoCredential
                ? "ASR：火山已配置"
                : "ASR：未配置";

    public string ModelLabel =>
        Settings.DemoMode
            ? "模型：本地演示"
            : Settings.HasDeepSeekCredential
                ? "模型：DeepSeek"
                : "模型：未配置";

    public async Task InitializeAsync()
    {
        try
        {
            Settings = await _runtime.SettingsStore.LoadAsync(
                _lifetimeCancellation.Token);
        }
        catch (InvalidDataException)
        {
            Settings = AppSettings.Default;
            StatusText = "加密设置无法读取，已切换到安全演示模式。";
        }

        try
        {
            var devices = await _runtime.AudioCapture.GetDevicesAsync(
                _lifetimeCancellation.Token);
            AudioDevices.Clear();
            foreach (var device in devices)
            {
                AudioDevices.Add(device);
            }

            SelectedAudioDevice = AudioDevices.FirstOrDefault(
                                      device =>
                                          device.Kind == AudioDeviceKind.SystemOutput)
                                  ?? AudioDevices.FirstOrDefault();
        }
        catch
        {
            StatusText = "无法枚举音频设备，仍可使用文本提问。";
        }

        if (!_runtime.KnowledgeDatabaseExistedAtStartup)
        {
            var defaultDocument = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "面经.docx");
            if (File.Exists(defaultDocument))
            {
                await ImportKnowledgeAsync(defaultDocument);
                return;
            }
        }
        else
        {
            KnowledgeStatus =
                $"本地知识库已就绪 · {_runtime.EmbeddingProviderName}";
        }

        StatusText = Settings.DemoMode
            ? "演示模式已就绪，可输入问题或测试音频开关。"
            : "云服务模式已就绪。";
    }

    public async Task ToggleCaptureAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (IsCapturing)
        {
            await StopCaptureAsync();
        }
        else
        {
            await StartCaptureAsync();
        }
    }

    public async Task AskAsync()
    {
        if (string.IsNullOrWhiteSpace(Transcript) || Transcript.Trim().Length < 2)
        {
            StatusText = "请先输入或转写面试官的问题。";
            return;
        }

        IsBusy = true;
        Answer = string.Empty;
        Evidence.Clear();
        _fallbackShown = false;
        StatusText = "正在检索面经并组织回答…";
        TimingText = "计时开始";
        var stopwatch = Stopwatch.StartNew();
        var operationId = Guid.NewGuid().ToString("N");
        Track(
            "question_started",
            operationId,
            ("mode", Settings.DemoMode ? "demo" : "cloud"));

        try
        {
            await foreach (var update in _coordinator.StreamAsync(
                               Transcript.Trim(),
                               _lifetimeCancellation.Token))
            {
                TimingText = $"{update.Elapsed.TotalSeconds:0.0}s";
                switch (update.Kind)
                {
                    case AnswerUpdateKind.Sources:
                        Evidence.Clear();
                        foreach (var hit in update.Evidence)
                        {
                            Evidence.Add(hit);
                        }

                        StatusText = $"已命中 {update.Evidence.Count} 条面经，正在生成…";
                        Track(
                            "retrieval_completed",
                            operationId,
                            ("durationMs", (long)update.Elapsed.TotalMilliseconds),
                            ("hitCount", update.Evidence.Count));
                        break;

                    case AnswerUpdateKind.LocalFallback:
                        Answer = update.Text;
                        _fallbackShown = true;
                        StatusText = "云端首字较慢，已先显示本地面经答案。";
                        Track(
                            "fallback_emitted",
                            operationId,
                            ("durationMs", (long)update.Elapsed.TotalMilliseconds),
                            ("usedFallback", true));
                        break;

                    case AnswerUpdateKind.CloudDelta:
                        if (_fallbackShown)
                        {
                            Answer = string.Empty;
                            _fallbackShown = false;
                        }

                        var wasEmpty = Answer.Length == 0;
                        Answer += update.Text;
                        StatusText = "正在流式生成回答…";
                        if (wasEmpty)
                        {
                            Track(
                                "first_answer_delta",
                                operationId,
                                ("durationMs", (long)update.Elapsed.TotalMilliseconds),
                                ("provider", Settings.DemoMode ? "demo" : "deepseek"));
                        }

                        break;

                    case AnswerUpdateKind.Error:
                        StatusText = update.Text;
                        Track(
                            "external_call_failed",
                            operationId,
                            ("provider", "deepseek"),
                            ("errorCode", "generation_failed"));
                        break;

                    case AnswerUpdateKind.Completed:
                        StatusText = "回答已完成，可直接口述或继续编辑问题。";
                        Track(
                            "question_completed",
                            operationId,
                            ("durationMs", (long)update.Elapsed.TotalMilliseconds),
                            ("usedFallback", _fallbackShown));
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消上一道问题。";
        }
        finally
        {
            TimingText = $"{stopwatch.Elapsed.TotalSeconds:0.0}s 完成";
            IsBusy = false;
        }
    }

    public async Task ImportKnowledgeAsync(string path)
    {
        IsBusy = true;
        StatusText = "正在解析并索引面经，原文件不会上传…";
        var operationId = Guid.NewGuid().ToString("N");
        try
        {
            var result = await _runtime.KnowledgeRepository.ImportDocxAsync(
                path,
                _lifetimeCancellation.Token);
            KnowledgeStatus =
                $"{result.SourceFileName} · {result.ChunkCount} 个知识块";
            StatusText =
                $"知识库导入完成，用时 {result.Duration.TotalSeconds:0.0}s。";
            Track(
                "knowledge_import_completed",
                operationId,
                ("durationMs", (long)result.Duration.TotalMilliseconds),
                ("chunkCount", result.ChunkCount),
                ("provider", result.EmbeddingProvider));
        }
        catch (Exception exception)
            when (exception is ArgumentException or InvalidDataException or IOException)
        {
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        settings.Validate();
        await _runtime.SettingsStore.SaveAsync(
            settings,
            _lifetimeCancellation.Token);
        Settings = settings;
        StatusText = settings.DemoMode
            ? "设置已加密保存，当前为演示模式。"
            : "设置已加密保存，云服务模式已启用。";
    }

    public void ReportUiError(string message) => StatusText = message;

    public async ValueTask DisposeAsync()
    {
        _lifetimeCancellation.Cancel();
        _coordinator.CancelCurrent();
        try
        {
            await _runtime.AudioCapture.StopAsync(CancellationToken.None);
            _audioChannel?.Writer.TryComplete();
            if (_audioPump is not null)
            {
                try
                {
                    await _audioPump;
                }
                catch (OperationCanceledException)
                {
                }
            }

            await CleanupCaptureAsync();
        }
        catch
        {
        }
        finally
        {
            _lifetimeCancellation.Dispose();
            await _runtime.DisposeAsync();
        }
    }

    private async Task StartCaptureAsync()
    {
        if (SelectedAudioDevice is null)
        {
            StatusText = "未找到可用音频设备，请使用文本提问。";
            return;
        }

        IsBusy = true;
        StatusText = Settings.DemoMode
            ? "正在启动本地音频采集…"
            : "正在连接火山实时语音识别…";
        try
        {
            _transcriber = _runtime.CreateTranscriber(() => Settings);
            _transcriber.TranscriptUpdated += OnTranscriptUpdated;
            _transcriber.TranscriptionFailed += OnTranscriptionFailed;
            await _transcriber.StartAsync(_lifetimeCancellation.Token);
            _audioChannel = Channel.CreateBounded<AudioFrame>(
                new BoundedChannelOptions(50)
                {
                    SingleReader = true,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.DropOldest,
                });
            _audioPump = PumpAudioAsync(
                _audioChannel.Reader,
                _transcriber,
                _lifetimeCancellation.Token);
            _runtime.AudioCapture.FrameCaptured += OnAudioFrameCaptured;
            _runtime.AudioCapture.CaptureFailed += OnAudioCaptureFailed;
            await _runtime.AudioCapture.StartAsync(
                SelectedAudioDevice,
                _lifetimeCancellation.Token);
            IsCapturing = true;
            StatusText = Settings.DemoMode
                ? "正在采集（演示模式不上传音频，停止后可编辑问题文本）。"
                : "正在实时转写面试官问题…";
        }
        catch (Exception exception)
        {
            StatusText = ToSafeCaptureError(exception);
            await CleanupCaptureAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StopCaptureAsync()
    {
        IsBusy = true;
        StatusText = "正在结束采集并确认问题…";
        try
        {
            await _runtime.AudioCapture.StopAsync(_lifetimeCancellation.Token);
            _audioChannel?.Writer.TryComplete();
            if (_audioPump is not null)
            {
                await _audioPump;
            }

            if (_transcriber is not null)
            {
                var final = await _transcriber.StopAsync(_lifetimeCancellation.Token);
                if (!string.IsNullOrWhiteSpace(final))
                {
                    Transcript = final;
                }
            }
        }
        catch
        {
            StatusText = "结束语音转写时发生错误，已保留当前转写文本。";
        }
        finally
        {
            IsCapturing = false;
            await CleanupCaptureAsync();
            IsBusy = false;
        }

        if (string.IsNullOrWhiteSpace(Transcript))
        {
            StatusText = Settings.DemoMode
                ? "演示模式不上传音频，请在左侧输入问题后点击生成。"
                : "未识别到问题，请编辑转写文本后点击生成。";
            return;
        }

        await AskAsync();
    }

    private async Task CleanupCaptureAsync()
    {
        _runtime.AudioCapture.FrameCaptured -= OnAudioFrameCaptured;
        _runtime.AudioCapture.CaptureFailed -= OnAudioCaptureFailed;
        _audioChannel?.Writer.TryComplete();
        if (_transcriber is not null)
        {
            _transcriber.TranscriptUpdated -= OnTranscriptUpdated;
            _transcriber.TranscriptionFailed -= OnTranscriptionFailed;
            try
            {
                await _transcriber.DisposeAsync();
            }
            finally
            {
                _transcriber = null;
            }
        }

        _audioChannel = null;
        _audioPump = null;
    }

    private static async Task PumpAudioAsync(
        ChannelReader<AudioFrame> reader,
        IStreamingTranscriber transcriber,
        CancellationToken cancellationToken)
    {
        await foreach (var frame in reader.ReadAllAsync(cancellationToken))
        {
            await transcriber.SendAudioAsync(frame, cancellationToken);
        }
    }

    private void OnAudioFrameCaptured(object? sender, AudioFrame frame) =>
        _audioChannel?.Writer.TryWrite(frame);

    private void OnTranscriptUpdated(object? sender, TranscriptUpdate update)
    {
        _ = Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Transcript = update.Text;
            StatusText = update.IsFinal
                ? "已识别一个完整问题片段。"
                : "正在实时转写面试官问题…";
        });
    }

    private void OnTranscriptionFailed(object? sender, string message) =>
        _ = Application.Current.Dispatcher.InvokeAsync(() => StatusText = message);

    private void OnAudioCaptureFailed(object? sender, string message) =>
        _ = Application.Current.Dispatcher.InvokeAsync(() => StatusText = message);

    private void Track(
        string eventName,
        string operationId,
        params (string Key, object? Value)[] fields)
    {
        var fieldMap = fields.ToDictionary(
            field => field.Key,
            field => field.Value,
            StringComparer.Ordinal);
        _ = TrackSafeAsync(
            new AppTelemetryEvent(eventName, operationId, fieldMap));
    }

    private async Task TrackSafeAsync(AppTelemetryEvent telemetryEvent)
    {
        try
        {
            await _runtime.Telemetry.TrackAsync(
                telemetryEvent,
                _lifetimeCancellation.Token);
        }
        catch
        {
            // Telemetry is intentionally non-blocking and contains no user text.
        }
    }

    private static string ToSafeCaptureError(Exception exception) =>
        exception switch
        {
            UnauthorizedAccessException => "没有访问该音频设备的权限。",
            ArgumentException => exception.Message,
            _ => "无法启动音频采集，请检查设备和云服务配置。",
        };
}
