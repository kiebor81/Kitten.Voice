namespace Kitten.Voice.Audio;

internal static class AudioOutputDispatcher
{
    internal static void Dispatch(AudioOutput output, string outputPath, float[] audio, int sampleRate)
    {
        switch (output)
        {
            case AudioOutput.Stream:
                AudioHelper.PlayFromMemory(audio, sampleRate);
                break;

            case AudioOutput.File:
                AudioHelper.SaveNormalizedWav(outputPath, audio, sampleRate);
                AudioHelper.Play(outputPath);
                break;

            case AudioOutput.FileOnly:
                AudioHelper.SaveNormalizedWav(outputPath, audio, sampleRate);
                break;
        }
    }
}
