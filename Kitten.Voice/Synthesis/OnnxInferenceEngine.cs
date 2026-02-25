using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Collections.Concurrent;

namespace Kitten.Voice.Synthesis;

/// <summary>
/// A simple ONNX inference engine for running the TTS model. It loads the model, prepares the inputs, and retrieves the output waveform.
/// </summary>
internal static class OnnxInferenceEngine
{
    private static readonly ConcurrentDictionary<string, Lazy<InferenceSession>> SessionCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Runs the ONNX model with the given inputs and returns the generated waveform as a float array.
    /// </summary>
    /// <param name="modelPath">The path to the ONNX model file.</param>
    /// <param name="tokenIds">The input token IDs for the model.</param>
    /// <param name="styleVector">The style vector for the model.</param>
    /// <param name="speed">The speed parameter for the model.</param>
    /// <returns>The generated waveform as a float array.</returns>
    internal static float[] Run(string modelPath, long[] tokenIds, float[] styleVector, float speed)
    {
        InferenceSession session = GetSession(modelPath);
        var inputs = BuildInputs(tokenIds, styleVector, speed);
        using var results = session.Run(inputs);
        return [.. results.First(r => r.Name == "waveform").AsEnumerable<float>()];
    }

    private static InferenceSession GetSession(string modelPath)
    {
        string key = Path.GetFullPath(modelPath);
        Lazy<InferenceSession> lazy = SessionCache.GetOrAdd(
            key,
            static path => new Lazy<InferenceSession>(() => new InferenceSession(path), LazyThreadSafetyMode.ExecutionAndPublication));

        return lazy.Value;
    }

    private static List<NamedOnnxValue> BuildInputs(long[] tokenIds, float[] styleVector, float speed)
    {
        var inputTensor = new DenseTensor<long>([1, tokenIds.Length]);
        for (int i = 0; i < tokenIds.Length; i++)
            inputTensor[0, i] = tokenIds[i];

        var styleTensor = new DenseTensor<float>([1, styleVector.Length]);
        for (int i = 0; i < styleVector.Length; i++)
            styleTensor[0, i] = styleVector[i];

        var speedTensor = new DenseTensor<float>([1]);
        speedTensor[0] = speed;

        return
        [
            NamedOnnxValue.CreateFromTensor("input_ids", inputTensor),
            NamedOnnxValue.CreateFromTensor("style", styleTensor),
            NamedOnnxValue.CreateFromTensor("speed", speedTensor),
        ];
    }
}
