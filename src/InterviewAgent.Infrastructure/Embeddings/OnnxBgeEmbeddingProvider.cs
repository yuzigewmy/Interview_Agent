using InterviewAgent.Core.Abstractions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace InterviewAgent.Infrastructure.Embeddings;

public sealed class OnnxBgeEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private const int BgeDimensions = 512;
    private readonly InferenceSession _session;
    private readonly BertWordPieceTokenizer _tokenizer;
    private readonly SemaphoreSlim _inferenceGate = new(1, 1);
    private readonly string _outputName;
    private bool _disposed;

    public OnnxBgeEmbeddingProvider(string modelPath, string vocabularyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(vocabularyPath);
        var fullModelPath = Path.GetFullPath(modelPath);
        if (!File.Exists(fullModelPath))
        {
            throw new FileNotFoundException("未找到 BGE ONNX 模型。", fullModelPath);
        }

        if (new FileInfo(fullModelPath).Length is <= 0 or > 500 * 1024 * 1024)
        {
            throw new InvalidDataException("BGE ONNX 模型大小无效。");
        }

        _tokenizer = BertWordPieceTokenizer.FromFile(vocabularyPath);
        using var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            InterOpNumThreads = 1,
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 8),
        };
        _session = new InferenceSession(fullModelPath, options);
        ValidateInputs(_session.InputNames);
        _outputName = SelectOutput(_session);
    }

    public string Name => "BAAI/bge-small-zh-v1.5-onnx";

    public int Dimensions => BgeDimensions;

    public async ValueTask<float[]> EmbedAsync(
        string text,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();

        await _inferenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                    () => EmbedCore(text, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _inferenceGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Dispose();
        _inferenceGate.Dispose();
    }

    private float[] EmbedCore(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var encoded = _tokenizer.Encode(text);
        long[] shape = [1, encoded.InputIds.LongLength];
        using var inputIds = OrtValue.CreateTensorValueFromMemory(encoded.InputIds, shape);
        using var attentionMask =
            OrtValue.CreateTensorValueFromMemory(encoded.AttentionMask, shape);
        using var tokenTypeIds =
            OrtValue.CreateTensorValueFromMemory(encoded.TokenTypeIds, shape);
        var inputs = new Dictionary<string, OrtValue>(StringComparer.Ordinal)
        {
            ["input_ids"] = inputIds,
            ["attention_mask"] = attentionMask,
        };
        if (_session.InputNames.Contains("token_type_ids", StringComparer.Ordinal))
        {
            inputs["token_type_ids"] = tokenTypeIds;
        }

        using var runOptions = new RunOptions();
        using var outputs = _session.Run(runOptions, inputs, [_outputName]);
        cancellationToken.ThrowIfCancellationRequested();
        var output = outputs[0];
        var shapeInfo = output.GetTensorTypeAndShape();
        if (shapeInfo.ElementDataType != TensorElementType.Float ||
            shapeInfo.Shape.Length is not (2 or 3) ||
            shapeInfo.Shape[^1] != BgeDimensions)
        {
            throw new InvalidDataException("BGE ONNX 输出形状无效。");
        }

        var values = output.GetTensorDataAsSpan<float>();
        if (values.Length < BgeDimensions)
        {
            throw new InvalidDataException("BGE ONNX 输出数据不完整。");
        }

        var embedding = values[..BgeDimensions].ToArray();
        Normalize(embedding);
        return embedding;
    }

    private static void ValidateInputs(IReadOnlyCollection<string> inputNames)
    {
        if (!inputNames.Contains("input_ids", StringComparer.Ordinal) ||
            !inputNames.Contains("attention_mask", StringComparer.Ordinal))
        {
            throw new InvalidDataException("BGE ONNX 模型缺少必要输入。");
        }
    }

    private static string SelectOutput(InferenceSession session)
    {
        var preferred = session.OutputNames.FirstOrDefault(
            name => name.Contains("last_hidden_state", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(preferred))
        {
            return preferred;
        }

        foreach (var (name, metadata) in session.OutputMetadata)
        {
            if (metadata.ElementType == typeof(float) &&
                metadata.Dimensions.LastOrDefault() == BgeDimensions)
            {
                return name;
            }
        }

        throw new InvalidDataException("BGE ONNX 模型没有可用的 512 维输出。");
    }

    private static void Normalize(float[] vector)
    {
        double squaredNorm = 0;
        foreach (var value in vector)
        {
            squaredNorm += value * value;
        }

        if (squaredNorm <= 0)
        {
            return;
        }

        var inverseNorm = (float)(1d / Math.Sqrt(squaredNorm));
        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] *= inverseNorm;
        }
    }
}
