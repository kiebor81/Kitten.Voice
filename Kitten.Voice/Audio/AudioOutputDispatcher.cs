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
                AudioHelper.SaveWav(outputPath, audio, sampleRate);
                AudioHelper.Play(outputPath);
                break;

            case AudioOutput.FileOnly:
                AudioHelper.SaveWav(outputPath, audio, sampleRate);
                break;
        }
    }
}
