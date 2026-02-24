using System.IO.Compression;

namespace Kitten.Voice.Embeddings;

/// <summary>
/// Loads voice style embeddings from .npz archives.
/// </summary>
public static class VoiceStore
{
    /// <summary>
    /// Loads a style row from a voice embedding as a flat vector.
    /// </summary>
    public static float[] LoadStyleVector(string npzPath, string voiceName, int styleRow = 0)
    {
        float[,] embedding = LoadEmbedding(npzPath, voiceName);
        int rows = embedding.GetLength(0);
        int cols = embedding.GetLength(1);
        int row = NormalizeRowIndex(styleRow, rows);

        float[] style = new float[cols];
        for (int i = 0; i < cols; i++)
            style[i] = embedding[row, i];
        return style;
    }

    /// <summary>
    /// Loads a style row selected from text/token length.
    /// </summary>
    public static float[] LoadStyleVectorForTokenCount(string npzPath, string voiceName, int tokenCount, int rowOffset = 0)
    {
        float[,] embedding = LoadEmbedding(npzPath, voiceName);
        int rows = embedding.GetLength(0);
        int cols = embedding.GetLength(1);
        int row = SelectRowForTokenCount(tokenCount, rows, rowOffset);

        float[] style = new float[cols];
        for (int i = 0; i < cols; i++)
            style[i] = embedding[row, i];
        return style;
    }

    /// <summary>
    /// Loads a style vector blended with a base row.
    /// blend = 0 uses <paramref name="baseRow"/>, blend = 1 uses only <paramref name="styleRow"/>.
    /// </summary>
    public static float[] LoadBlendedStyleVector(string npzPath, string voiceName, int styleRow, float blend, int baseRow = 0)
    {
        float[,] embedding = LoadEmbedding(npzPath, voiceName);
        int rows = embedding.GetLength(0);
        int cols = embedding.GetLength(1);
        int targetRow = NormalizeRowIndex(styleRow, rows);
        int sourceRow = NormalizeRowIndex(baseRow, rows);
        float t = Math.Clamp(blend, 0f, 1f);

        float[] style = new float[cols];
        for (int i = 0; i < cols; i++)
            style[i] = (embedding[sourceRow, i] * (1f - t)) + (embedding[targetRow, i] * t);
        return style;
    }

    /// <summary>
    /// Loads the full [rows, cols] voice embedding from a .npz archive.
    /// </summary>
    public static float[,] LoadEmbedding(string npzPath, string voiceName)
    {
        using var archive = ZipFile.OpenRead(npzPath);
        var entry = archive.GetEntry(voiceName + ".npy")
            ?? throw new FileNotFoundException($"Voice '{voiceName}' not found in {npzPath}");

        using var stream = entry.Open();
        return NpyReader.ReadFloat32(stream);
    }

    private static int NormalizeRowIndex(int row, int rows)
    {
        if (rows <= 0)
            return 0;

        int mod = row % rows;
        return mod < 0 ? mod + rows : mod;
    }

    private static int SelectRowForTokenCount(int tokenCount, int rows, int rowOffset)
    {
        if (rows <= 0)
            return 0;

        int lengthBased = Math.Max(1, tokenCount - 1);
        return NormalizeRowIndex(lengthBased + rowOffset, rows);
    }
}
