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
using InterviewAgent.Infrastructure.Documents;

namespace InterviewAgent.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly AppRuntime _runtime;
    private readonly AnswerCoordinator _coordinator;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private readonly AudioLevelSmoother _audioLevelSmoother = new();
    private AppSettings _settings = AppSettings.Default;
    private AudioDevice? _selectedAudioDevice;
    private string _transcript = string.Empty;
    private string _answer = string.Empty;
    private string _overlayErrorText = string.Empty;
    private string _statusText = "正在初始化…";
    private string _knowledgeStatus = "尚未载入";
    private string _timingText = "等待问题";
    private double _audioLevel;
    private bool _isCapturing;
    private bool _isBusy;
    private bool _fallbackShown;
    private bool _isOverlayTurnVisible;
    private long _lastAudioLevelUpdateTimestamp;
    private IStreamingTranscriber? _transcriber;
    private Channel<AudioFrame>? _audioChannel;
    private Task? _audioPump;

    public MainViewModel(AppRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _coordinator = new AnswerCoordinator(
            runtime.KnowledgeRepository,
            () => runtime.CreateAnswerGenerator(() => Settings),
            questionCorrector: runtime.QuestionCorrector,
            interviewSession: runtime.InterviewSession);
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
        set
        {
            if (SetProperty(ref _transcript, value))
            {
                RaiseOverlayProperties();
            }
        }
    }

    public string Answer
    {
        get => _answer;
        private set
        {
            if (SetProperty(ref _answer, value))
            {
                RaiseOverlayProperties();
            }
        }
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
                RaisePropertyChanged(nameof(CanRefreshAudioDevices));
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
                RaisePropertyChanged(nameof(CanRefreshAudioDevices));
                RaisePropertyChanged(nameof(CanExportInterview));
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public double AudioLevel
    {
        get => _audioLevel;
        private set => SetProperty(ref _audioLevel, Math.Clamp(value, 0, 1));
    }

    public bool IsOverlayTurnVisible
    {
        get => _isOverlayTurnVisible;
        private set
        {
            if (SetProperty(ref _isOverlayTurnVisible, value))
            {
                RaiseOverlayProperties();
            }
        }
    }

    public string OverlayQuestion => Transcript;

    public string OverlayAnswer =>
        string.IsNullOrWhiteSpace(Answer)
            ? _overlayErrorText
            : Answer;

    public bool HasOverlayQuestion =>
        IsOverlayTurnVisible &&
        !string.IsNullOrWhiteSpace(OverlayQuestion);

    public bool HasOverlayAnswer =>
        IsOverlayTurnVisible &&
        !string.IsNullOrWhiteSpace(OverlayAnswer);

    public bool IsOverlayConversationVisible =>
        HasOverlayQuestion || HasOverlayAnswer;

    public bool CanRefreshAudioDevices => !IsBusy && !IsCapturing;

    public int InterviewRecordCount => _runtime.InterviewSession.Count;

    public bool CanExportInterview => InterviewRecordCount > 0 && !IsBusy;

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
            : Settings.HasModelConfiguration
                ? $"模型：{Settings.ModelProvider} / {Settings.ModelName}"
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

        await LoadAudioDevicesAsync(updateStatus: false);

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

        ShowOverlayTurn();
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
                        ClearOverlayError();
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
                        ClearOverlayError();
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
                                ("provider", Settings.DemoMode
                                    ? "demo"
                                    : "configured-model"));
                        }

                        break;

                    case AnswerUpdateKind.Error:
                        StatusText = update.Text;
                        SetOverlayError("回答生成失败，请检查模型设置后重试。");
                        Track(
                            "external_call_failed",
                            operationId,
                            ("provider", "configured-model"),
                            ("errorCode", "generation_failed"));
                        break;

                    case AnswerUpdateKind.Completed:
                        StatusText = "回答已完成，可直接口述或继续编辑问题。";
                        RaisePropertyChanged(nameof(InterviewRecordCount));
                        RaisePropertyChanged(nameof(CanExportInterview));
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
        catch
        {
            SetOverlayError("回答生成失败，请检查模型设置后重试。");
            throw;
        }
        finally
        {
            TimingText = $"{stopwatch.Elapsed.TotalSeconds:0.0}s 完成";
            IsBusy = false;
        }
    }

    public async Task RefreshAudioDevicesAsync()
    {
        if (!CanRefreshAudioDevices)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await LoadAudioDevicesAsync(updateStatus: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public string CreateSuggestedExportFileName(DateTimeOffset timestamp)
    {
        var firstQuestion = _runtime.InterviewSession
                                .Snapshot()
                                .FirstOrDefault()
                                ?.CorrectedQuestion
                            ?? "面试记录";
        return InterviewExportFileName.Create(firstQuestion, timestamp);
    }

    public async Task ExportInterviewAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var records = _runtime.InterviewSession.Snapshot();
        if (records.Count == 0)
        {
            throw new InvalidOperationException("当前面试还没有可导出的问答记录。");
        }

        IsBusy = true;
        StatusText = "正在生成面试问答 DOCX…";
        try
        {
            await _runtime.InterviewTranscriptExporter.ExportAsync(
                path,
                records,
                _lifetimeCancellation.Token);
            StatusText =
                $"已导出 {records.Count} 道面试问答：{Path.GetFileName(path)}";
        }
        finally
        {
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
        await _settingsSaveGate.WaitAsync(_lifetimeCancellation.Token);
        AppSettings merged;
        try
        {
            merged = settings with
            {
                FloatingOverlayDiameter = Settings.FloatingOverlayDiameter,
                FloatingOverlayLeft = Settings.FloatingOverlayLeft,
                FloatingOverlayTop = Settings.FloatingOverlayTop,
            };
            await _runtime.SettingsStore.SaveAsync(
                merged,
                _lifetimeCancellation.Token);
            Settings = merged;
        }
        finally
        {
            _settingsSaveGate.Release();
        }

        StatusText = merged.DemoMode
            ? "设置已加密保存，当前为演示模式。"
            : "设置已加密保存，云服务模式已启用。";
    }

    public async Task SaveOverlayPlacementAsync(
        double left,
        double top,
        double diameter,
        CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token,
            cancellationToken);
        await _settingsSaveGate.WaitAsync(linkedCancellation.Token);
        try
        {
            var updated = Settings with
            {
                FloatingOverlayLeft = left,
                FloatingOverlayTop = top,
                FloatingOverlayDiameter = diameter,
            };
            updated.Validate();
            await _runtime.SettingsStore.SaveAsync(
                updated,
                linkedCancellation.Token);
            Settings = updated;
        }
        finally
        {
            _settingsSaveGate.Release();
        }
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
            _runtime.InterviewSession.Clear();
            _settingsSaveGate.Dispose();
            _lifetimeCancellation.Dispose();
            await _runtime.DisposeAsync();
        }
    }

    private async Task LoadAudioDevicesAsync(bool updateStatus)
    {
        var previousDeviceId = SelectedAudioDevice?.Id;
        try
        {
            var devices = await _runtime.AudioCapture.GetDevicesAsync(
                _lifetimeCancellation.Token);
            AudioDevices.Clear();
            foreach (var device in devices)
            {
                AudioDevices.Add(device);
            }

            SelectedAudioDevice = AudioDeviceSelection.Select(
                devices,
                previousDeviceId);
            if (updateStatus)
            {
                StatusText = devices.Count == 0
                    ? "未发现可用音频来源，仍可使用文本提问。"
                    : $"音频来源已刷新，共发现 {devices.Count} 个设备。";
            }
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch
        {
            StatusText = "无法枚举音频设备，仍可使用文本提问。";
        }
    }

    private async Task StartCaptureAsync()
    {
        if (SelectedAudioDevice is null)
        {
            StatusText = "未找到可用音频设备，请使用文本提问。";
            return;
        }

        BeginNewOverlayTurn();
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

        ShowOverlayTurn();
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
        ResetAudioLevel();
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

    private void OnAudioFrameCaptured(object? sender, AudioFrame frame)
    {
        _audioChannel?.Writer.TryWrite(frame);

        var measured = AudioLevelMeter.MeasureNormalized(frame.Pcm16);
        var smoothed = _audioLevelSmoother.Update(measured);
        var timestamp = Stopwatch.GetTimestamp();
        var minimumInterval = Stopwatch.Frequency / 15;
        if (timestamp - _lastAudioLevelUpdateTimestamp < minimumInterval)
        {
            return;
        }

        _lastAudioLevelUpdateTimestamp = timestamp;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        _ = dispatcher.InvokeAsync(() => AudioLevel = smoothed);
    }

    private void OnTranscriptUpdated(object? sender, TranscriptUpdate update)
    {
        _ = Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Transcript = update.Text;
            ShowOverlayTurn();
            StatusText = update.IsFinal
                ? "已识别一个完整问题片段。"
                : "正在实时转写面试官问题…";
        });
    }

    private void OnTranscriptionFailed(object? sender, string message) =>
        _ = Application.Current.Dispatcher.InvokeAsync(() => StatusText = message);

    private void OnAudioCaptureFailed(object? sender, string message) =>
        _ = Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ResetAudioLevel();
            StatusText = message;
        });

    private void BeginNewOverlayTurn()
    {
        IsOverlayTurnVisible = false;
        ClearOverlayError();
        Transcript = string.Empty;
        Answer = string.Empty;
    }

    private void ShowOverlayTurn()
    {
        ClearOverlayError();
        IsOverlayTurnVisible = true;
    }

    private void SetOverlayError(string message)
    {
        _overlayErrorText = message;
        IsOverlayTurnVisible = true;
        RaiseOverlayProperties();
    }

    private void ClearOverlayError()
    {
        if (_overlayErrorText.Length == 0)
        {
            return;
        }

        _overlayErrorText = string.Empty;
        RaiseOverlayProperties();
    }

    private void RaiseOverlayProperties()
    {
        RaisePropertyChanged(nameof(OverlayQuestion));
        RaisePropertyChanged(nameof(OverlayAnswer));
        RaisePropertyChanged(nameof(HasOverlayQuestion));
        RaisePropertyChanged(nameof(HasOverlayAnswer));
        RaisePropertyChanged(nameof(IsOverlayConversationVisible));
    }

    private void ResetAudioLevel()
    {
        _audioLevelSmoother.Reset();
        _lastAudioLevelUpdateTimestamp = 0;
        AudioLevel = 0;
    }

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
