namespace Kitten.Voice;

/// <summary>
/// Post-processing effects applied to synthesized waveforms.
/// </summary>
public static class WaveformProcessor
{
    /// <summary>
    /// Applies volume scaling to audio samples (in-place).
    /// </summary>
    public static void ApplyVolume(float[] samples, float volume)
    {
        if (Math.Abs(volume - 1.0f) < 0.001f) return;

        for (int i = 0; i < samples.Length; i++)
            samples[i] = Math.Clamp(samples[i] * volume, -1.0f, 1.0f);
    }

    /// <summary>
    /// Reduces gain when peak amplitude exceeds <paramref name="maxAbs"/>.
    /// </summary>
    public static void ApplyPeakLimiter(float[] samples, float maxAbs = 0.92f)
    {
        if (samples.Length == 0) return;

        float peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float a = Math.Abs(samples[i]);
            if (a > peak) peak = a;
        }

        if (peak <= maxAbs || peak < 1e-6f) return;

        float gain = maxAbs / peak;
        for (int i = 0; i < samples.Length; i++)
            samples[i] *= gain;
    }

    /// <summary>
    /// Applies gentle analog-style saturation to tame peaks before limiting.
    /// </summary>
    public static void ApplySoftClip(float[] samples, float drive = 1.10f)
    {
        if (samples.Length == 0 || drive <= 1.0f) return;

        float norm = MathF.Atan(drive);
        for (int i = 0; i < samples.Length; i++)
            samples[i] = MathF.Atan(samples[i] * drive) / norm;
    }

    /// <summary>
    /// Shifts pitch by the specified number of semitones.
    /// Uses resampling to change pitch while approximately preserving duration.
    /// </summary>
    public static float[] ApplyPitchShift(float[] samples, int sampleRate, float semitones)
    {
        if (Math.Abs(semitones) < 0.01f) return samples;

        // Pitch ratio: +12 semitones = 2x frequency
        double ratio = Math.Pow(2.0, semitones / 12.0);

        // Resample to shift pitch
        float[] pitched = Resample(samples, ratio);

        // Time-stretch back to original duration using simple overlap-add
        return TimeStretch(pitched, sampleRate, 1.0 / ratio);
    }

    /// <summary>
    /// Generates silence samples for the specified duration.
    /// </summary>
    public static float[] GenerateSilence(int sampleRate, TimeSpan duration)
    {
        int count = (int)(sampleRate * duration.TotalSeconds);
        return new float[count];
    }

    /// <summary>
    /// Concatenates multiple audio segments into a single waveform.
    /// </summary>
    public static float[] Concatenate(params float[][] segments)
    {
        int totalLength = 0;
        foreach (var seg in segments)
            totalLength += seg.Length;

        var result = new float[totalLength];
        int offset = 0;
        foreach (var seg in segments)
        {
            Array.Copy(seg, 0, result, offset, seg.Length);
            offset += seg.Length;
        }
        return result;
    }

    /// <summary>
    /// Resamples audio by the given ratio using linear interpolation.
    /// Ratio &gt; 1 compresses (higher pitch), &lt; 1 stretches (lower pitch).
    /// </summary>
    private static float[] Resample(float[] samples, double ratio)
    {
        int newLength = (int)(samples.Length / ratio);
        if (newLength <= 0) return [];

        var result = new float[newLength];
        for (int i = 0; i < newLength; i++)
        {
            double srcIndex = i * ratio;
            int idx = (int)srcIndex;
            float frac = (float)(srcIndex - idx);

            if (idx + 1 < samples.Length)
                result[i] = samples[idx] * (1 - frac) + samples[idx + 1] * frac;
            else if (idx < samples.Length)
                result[i] = samples[idx];
        }
        return result;
    }

    /// <summary>
    /// Simple time-stretch using overlap-add with fixed window size.
    /// </summary>
    private static float[] TimeStretch(float[] samples, int sampleRate, double stretchRatio)
    {
        if (Math.Abs(stretchRatio - 1.0) < 0.01) return samples;

        int windowSize = sampleRate / 25; // 40ms windows
        int hopIn = windowSize / 2;
        int hopOut = (int)(hopIn * stretchRatio);
        int outputLength = (int)(samples.Length * stretchRatio);

        var result = new float[outputLength];
        var windowWeights = new float[outputLength];

        for (int inPos = 0, outPos = 0;
             inPos + windowSize <= samples.Length && outPos + windowSize <= outputLength;
             inPos += hopIn, outPos += hopOut)
        {
            for (int j = 0; j < windowSize; j++)
            {
                // Hann window
                float w = 0.5f * (1 - MathF.Cos(2 * MathF.PI * j / windowSize));
                result[outPos + j] += samples[inPos + j] * w;
                windowWeights[outPos + j] += w;
            }
        }

        // Normalize by window weights to avoid volume artifacts
        for (int i = 0; i < outputLength; i++)
        {
            if (windowWeights[i] > 0.001f)
                result[i] /= windowWeights[i];
        }

        return result;
    }
}
