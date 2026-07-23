using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using InterviewAgent.Core.Abstractions;
using InterviewAgent.Core.Audio;

namespace InterviewAgent.Infrastructure.Volcano;

public sealed class VolcanoStreamingTranscriber : IStreamingTranscriber
{
    private const int MaxWebSocketMessageBytes = 2 * 1024 * 1024;
    private readonly Func<VolcanoAsrOptions> _optionsAccessor;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly object _stateGate = new();
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _sessionCancellation;
    private Task? _receiveTask;
    private int _sequence = 1;
    private string _latestTranscript = string.Empty;

    public VolcanoStreamingTranscriber(Func<VolcanoAsrOptions> optionsAccessor)
    {
        _optionsAccessor = optionsAccessor ??
            throw new ArgumentNullException(nameof(optionsAccessor));
    }

    public event EventHandler<TranscriptUpdate>? TranscriptUpdated;

    public event EventHandler<string>? TranscriptionFailed;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var options = _optionsAccessor();
        options.Validate();
        lock (_stateGate)
        {
            if (_socket is not null)
            {
                throw new InvalidOperationException("语音转写已启动。");
            }

            _socket = new ClientWebSocket();
            _sessionCancellation = new CancellationTokenSource();
            _sequence = 1;
            _latestTranscript = string.Empty;
        }

        var socket = _socket;
        try
        {
            ConfigureHeaders(socket.Options, options);
            using var connectCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCancellation.CancelAfter(TimeSpan.FromSeconds(6));
            await socket.ConnectAsync(VolcanoAsrOptions.Endpoint, connectCancellation.Token);

            var request = JsonSerializer.Serialize(new
            {
                user = new
                {
                    uid = string.IsNullOrWhiteSpace(options.AppKey)
                        ? "interview-agent"
                        : options.AppKey,
                },
                audio = new
                {
                    format = "pcm",
                    codec = "raw",
                    rate = 16_000,
                    bits = 16,
                    channel = 1,
                },
                request = new
                {
                    model_name = "bigmodel",
                    enable_nonstream = true,
                    enable_itn = true,
                    enable_punc = true,
                    enable_ddc = true,
                    show_utterances = true,
                    end_window_size = 500,
                },
            });
            await SendFrameAsync(
                socket,
                VolcanoAsrProtocol.EncodeFullRequest(request),
                cancellationToken);

            var first = await ReceiveFrameAsync(socket, connectCancellation.Token);
            ProcessFrame(first);
            _receiveTask = ReceiveLoopAsync(socket, _sessionCancellation.Token);
        }
        catch
        {
            await ResetAsync(socket);
            throw;
        }
    }

    public async ValueTask SendAudioAsync(
        AudioFrame frame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.SampleRate != 16_000 ||
            frame.Channels != 1 ||
            frame.Pcm16.Length == 0)
        {
            throw new ArgumentException(
                "火山 ASR 只接受 16 kHz、16-bit、单声道 PCM。",
                nameof(frame));
        }

        var socket = GetOpenSocket();
        var sequence = Interlocked.Increment(ref _sequence);
        await SendFrameAsync(
            socket,
            VolcanoAsrProtocol.EncodeAudio(sequence, frame.Pcm16),
            cancellationToken);
    }

    public async Task<string> StopAsync(CancellationToken cancellationToken)
    {
        ClientWebSocket? socket;
        Task? receiveTask;
        lock (_stateGate)
        {
            socket = _socket;
            receiveTask = _receiveTask;
        }

        if (socket is null)
        {
            return _latestTranscript;
        }

        try
        {
            if (socket.State == WebSocketState.Open)
            {
                var finalSequence = -Interlocked.Increment(ref _sequence);
                await SendFrameAsync(
                    socket,
                    VolcanoAsrProtocol.EncodeAudio(finalSequence, ReadOnlySpan<byte>.Empty),
                    cancellationToken);
            }

            if (receiveTask is not null)
            {
                try
                {
                    await receiveTask.WaitAsync(TimeSpan.FromSeconds(4), cancellationToken);
                }
                catch (TimeoutException)
                {
                    // The best partial transcript is returned after the bounded wait.
                }
            }
        }
        finally
        {
            await ResetAsync(socket);
        }

        return _latestTranscript;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync(CancellationToken.None);
        }
        catch
        {
            // Dispose must not surface a network shutdown failure.
        }

        _sendGate.Dispose();
    }

    private async Task ReceiveLoopAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        try
        {
            while (socket.State == WebSocketState.Open &&
                   !cancellationToken.IsCancellationRequested)
            {
                var frame = await ReceiveFrameAsync(socket, cancellationToken);
                ProcessFrame(frame);
                if (frame.IsLast)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            TranscriptionFailed?.Invoke(this, ToSafeError(exception));
        }
    }

    private void ProcessFrame(VolcanoServerFrame frame)
    {
        if (frame.MessageType == 0b1111)
        {
            throw new InvalidOperationException(
                $"火山 ASR 拒绝请求（错误码 {frame.ErrorCode?.ToString() ?? "unknown"}）。");
        }

        if (frame.MessageType != 0b1001)
        {
            return;
        }

        var update = VolcanoTranscriptParser.Parse(frame.Payload, frame.IsLast);
        if (string.IsNullOrWhiteSpace(update.Text))
        {
            return;
        }

        _latestTranscript = update.Text;
        TranscriptUpdated?.Invoke(this, update);
    }

    private ClientWebSocket GetOpenSocket()
    {
        lock (_stateGate)
        {
            if (_socket is null || _socket.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("语音转写尚未启动。");
            }

            return _socket;
        }
    }

    private async Task SendFrameAsync(
        ClientWebSocket socket,
        byte[] frame,
        CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(
                frame,
                WebSocketMessageType.Binary,
                endOfMessage: true,
                cancellationToken);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private static async Task<VolcanoServerFrame> ReceiveFrameAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var output = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new EndOfStreamException("火山 ASR 已关闭连接。");
            }

            if (result.MessageType != WebSocketMessageType.Binary)
            {
                throw new InvalidDataException("火山 ASR 返回了非二进制消息。");
            }

            output.Write(buffer, 0, result.Count);
            if (output.Length > MaxWebSocketMessageBytes)
            {
                throw new InvalidDataException("火山 ASR 消息超过大小上限。");
            }

            if (result.EndOfMessage)
            {
                return VolcanoAsrProtocol.DecodeServerFrame(output.ToArray());
            }
        }
    }

    private async Task ResetAsync(ClientWebSocket socket)
    {
        _sessionCancellation?.Cancel();
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await socket.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "session complete",
                    CancellationToken.None);
            }
            catch (WebSocketException)
            {
            }
        }

        socket.Dispose();
        lock (_stateGate)
        {
            if (ReferenceEquals(_socket, socket))
            {
                _socket = null;
                _receiveTask = null;
                _sessionCancellation?.Dispose();
                _sessionCancellation = null;
            }
        }
    }

    private static void ConfigureHeaders(
        ClientWebSocketOptions socketOptions,
        VolcanoAsrOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            socketOptions.SetRequestHeader("X-Api-Key", options.ApiKey);
            socketOptions.SetRequestHeader("Authorization", $"Bearer {options.ApiKey}");
        }
        else
        {
            socketOptions.SetRequestHeader("X-Api-App-Key", options.AppKey);
            socketOptions.SetRequestHeader("X-Api-Access-Key", options.AccessKey);
        }

        var requestId = Guid.NewGuid().ToString();
        socketOptions.SetRequestHeader("X-Api-Resource-Id", options.ResourceId);
        socketOptions.SetRequestHeader("X-Api-Request-Id", requestId);
        socketOptions.SetRequestHeader("X-Api-Connect-Id", requestId);
    }

    private static string ToSafeError(Exception exception) =>
        exception switch
        {
            WebSocketException => "火山 ASR 网络连接中断。",
            InvalidDataException => "火山 ASR 返回了无法解析的数据。",
            _ => "火山 ASR 转写失败。",
        };
}

