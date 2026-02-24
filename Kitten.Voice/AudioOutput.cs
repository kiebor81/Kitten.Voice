namespace Kitten.Voice;

/// <summary>
/// Controls how audio is output after synthesis.
/// </summary>
public enum AudioOutput
{
    /// <summary>Stream directly to speakers from memory; no file written.</summary>
    Stream,

    /// <summary>Save to a WAV file, then play from disk.</summary>
    File,

    /// <summary>Save to a WAV file without playing.</summary>
    FileOnly,
}