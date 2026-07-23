using InterviewAgent.Core.Abstractions;
using InterviewAgent.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace InterviewAgent.Infrastructure.Audio;

public sealed class WasapiAudioCaptureService : IAudioCaptureService
{
    private const int TargetSampleRate = 16_000;
    private readonly object _gate = new();
    private IWaveIn? _capture;
    private MMDevice? _device;
    private BufferedWaveProvider? _bufferedProvider;
    private IWaveProvider? _pcmProvider;

    public event EventHandler<AudioFrame>? FrameCaptured;

    public event EventHandler<string>? CaptureFailed;

    public bool IsCapturing
    {
        get
        {
            lock (_gate)
            {
                return _capture is not null;
            }
        }
    }

    public Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var enumerator = new MMDeviceEnumerator();
        var devices = new List<AudioDevice>();
        devices.AddRange(
            enumerator
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(device => new AudioDevice(
                    device.ID,
                    device.FriendlyName,
                    AudioDeviceKind.SystemOutput)));
        devices.AddRange(
            enumerator
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Select(device => new AudioDevice(
                    device.ID,
                    device.FriendlyName,
                    AudioDeviceKind.Microphone)));
        return Task.FromResult<IReadOnlyList<AudioDevice>>(devices);
    }

    public Task StartAsync(AudioDevice device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_capture is not null)
            {
                throw new InvalidOperationException("音频采集已启动。");
            }

            using var enumerator = new MMDeviceEnumerator();
            _device = enumerator.GetDevice(device.Id);
            _capture = device.Kind == AudioDeviceKind.SystemOutput
                ? new WasapiLoopbackCapture(_device)
                : new WasapiCapture(_device);
            _bufferedProvider = new BufferedWaveProvider(_capture.WaveFormat)
            {
                BufferDuration = TimeSpan.FromSeconds(2),
                DiscardOnBufferOverflow = true,
                ReadFully = false,
            };
            ISampleProvider samples = _bufferedProvider.ToSampleProvider();
            if (samples.WaveFormat.Channels != 1)
            {
                samples = new MonoSampleProvider(samples);
            }

            samples = new WdlResamplingSampleProvider(samples, TargetSampleRate);
            _pcmProvider = new SampleToWaveProvider16(samples);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IWaveIn? capture;
        lock (_gate)
        {
            capture = _capture;
            _capture = null;
        }

        if (capture is not null)
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;
            capture.StopRecording();
            capture.Dispose();
        }

        lock (_gate)
        {
            _device?.Dispose();
            _device = null;
            _bufferedProvider = null;
            _pcmProvider = null;
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        try
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort release during application shutdown.
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs eventArgs)
    {
        BufferedWaveProvider? buffered;
        IWaveProvider? pcm;
        lock (_gate)
        {
            buffered = _bufferedProvider;
            pcm = _pcmProvider;
        }

        if (buffered is null || pcm is null || eventArgs.BytesRecorded == 0)
        {
            return;
        }

        try
        {
            buffered.AddSamples(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
            var output = new byte[3_200];
            for (var iteration = 0; iteration < 4; iteration++)
            {
                var read = pcm.Read(output, 0, output.Length);
                if (read <= 0)
                {
                    break;
                }

                var bytes = output.AsSpan(0, read).ToArray();
                FrameCaptured?.Invoke(
                    this,
                    new AudioFrame(
                        bytes,
                        TargetSampleRate,
                        1,
                        DateTimeOffset.UtcNow));
                if (read < output.Length)
                {
                    break;
                }
            }
        }
        catch
        {
            CaptureFailed?.Invoke(this, "音频格式转换失败，请切换音频设备后重试。");
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs eventArgs)
    {
        if (eventArgs.Exception is not null)
        {
            CaptureFailed?.Invoke(this, "音频设备已停止，请重新选择设备。");
        }
    }
}

