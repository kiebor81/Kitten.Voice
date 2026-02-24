using NAudio.Wave;

namespace Kitten.Voice.Audio;

/// <summary>
/// Handles WAV file writing, normalization, and playback.
/// </summary>
public static class AudioHelper
{
    private const float NormalizationTarget = 0.95f;
    private const float SilenceThreshold = 0.02f;
    private const int FadeInMs = 10;
    private const int FadeOutMs = 150;
    private const int TrailingPauseMs = 200;
    private const int AnalysisWindowMs = 20;

    /// <summary>
    /// Trims, normalizes, and applies fades to raw audio samples.
    /// </summary>
    public static float[] ProcessAudio(float[] samples, int sampleRate)
    {
        samples = TrimTail(samples, sampleRate);
        if (samples.Length == 0) return samples;

        AppendSilence(ref samples, sampleRate, TrailingPauseMs);
        Normalize(samples);
        ApplyFadeIn(samples, sampleRate, FadeInMs);
        ApplyFadeOut(samples, sampleRate, FadeOutMs);
        return samples;
    }

    /// <summary>
    /// Processes raw audio and saves as 16-bit WAV.
    /// </summary>
    public static void SaveNormalizedWav(string path, float[] samples, int sampleRate)
    {
        samples = ProcessAudio(samples, sampleRate);
        if (samples.Length == 0) return;
        WriteWav(path, samples, sampleRate);
    }

    /// <summary>
    /// Plays processed audio samples directly from memory; no file I/O.
    /// </summary>
    public static void PlayFromMemory(float[] samples, int sampleRate)
    {
        if (samples.Length == 0) return;

        // Convert float samples to 16-bit PCM bytes
        var pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short value = (short)(samples[i] * short.MaxValue);
            pcm[i * 2] = (byte)value;
            pcm[i * 2 + 1] = (byte)(value >> 8);
        }

        var format = new WaveFormat(sampleRate, 16, 1);
        using var stream = new RawSourceWaveStream(new MemoryStream(pcm), format);
        using var outputDevice = new WaveOutEvent();
        outputDevice.Init(stream);
        outputDevice.Play();

        while (outputDevice.PlaybackState == PlaybackState.Playing)
            Thread.Sleep(100);
    }

    /// <summary>
    /// Plays a WAV file from disk synchronously.
    /// </summary>
    public static void Play(string path)
    {
        using var audioFile = new AudioFileReader(path);
        using var outputDevice = new WaveOutEvent();
        outputDevice.Init(audioFile);
        outputDevice.Play();

        while (outputDevice.PlaybackState == PlaybackState.Playing)
            Thread.Sleep(100);
    }

    private static void AppendSilence(ref float[] samples, int sampleRate, int milliseconds)
    {
        int count = sampleRate * milliseconds / 1000;
        Array.Resize(ref samples, samples.Length + count);
    }

    private static void Normalize(float[] samples)
    {
        float absMax = Math.Max(Math.Abs(samples.Min()), Math.Abs(samples.Max()));
        if (absMax <= 0) return;

        float gain = NormalizationTarget / absMax;
        for (int i = 0; i < samples.Length; i++)
            samples[i] *= gain;
    }

    private static void ApplyFadeIn(float[] samples, int sampleRate, int milliseconds)
    {
        int count = Math.Min(sampleRate * milliseconds / 1000, samples.Length);
        for (int i = 0; i < count; i++)
            samples[i] *= (float)i / count;
    }

    private static void ApplyFadeOut(float[] samples, int sampleRate, int milliseconds)
    {
        int count = Math.Min(sampleRate * milliseconds / 1000, samples.Length);
        for (int i = 0; i < count; i++)
            samples[samples.Length - 1 - i] *= (float)i / count;
    }

    private static void WriteWav(string path, float[] samples, int sampleRate)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, 1));
        foreach (var sample in samples)
            writer.WriteSample(sample);
    }

    /// <summary>
    /// Trims trailing samples where signal energy drops below a threshold.
    /// </summary>
    private static float[] TrimTail(float[] samples, int sampleRate)
    {
        int windowSize = sampleRate * AnalysisWindowMs / 1000;
        if (windowSize == 0) return samples;

        float peakRms = ComputePeakRms(samples, windowSize);
        if (peakRms < 1e-6f) return samples;

        float threshold = peakRms * SilenceThreshold;
        int lastActive = FindLastActiveWindow(samples, windowSize, threshold);

        return samples[..lastActive];
    }

    private static float ComputePeakRms(float[] samples, int windowSize)
    {
        float peakRms = 0;
        for (int start = 0; start + windowSize <= samples.Length; start += windowSize)
        {
            float rms = WindowRms(samples, start, windowSize);
            if (rms > peakRms) peakRms = rms;
        }
        return peakRms;
    }

    private static int FindLastActiveWindow(float[] samples, int windowSize, float threshold)
    {
        for (int start = samples.Length - windowSize; start >= 0; start -= windowSize)
        {
            if (WindowRms(samples, start, windowSize) > threshold)
                return Math.Min(start + windowSize * 3, samples.Length);
        }
        return samples.Length;
    }

    private static float WindowRms(float[] samples, int start, int windowSize)
    {
        float sum = 0;
        for (int j = start; j < start + windowSize; j++)
            sum += samples[j] * samples[j];
        return MathF.Sqrt(sum / windowSize);
    }
}
