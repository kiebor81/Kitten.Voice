namespace Kitten.Voice;

/// <summary>
/// Reads NumPy .npy binary files containing 2D float32 arrays.
/// </summary>
public static class NpyReader
{
    /// <summary>
    /// Reads a .npy file stream and returns a 2D float32 array.
    /// </summary>
    public static float[,] ReadFloat32(Stream stream)
    {
        using var reader = new BinaryReader(stream);

        reader.ReadBytes(6); // magic: \x93NUMPY
        byte major = reader.ReadByte();
        reader.ReadByte();   // minor version

        int headerLen = major >= 2
            ? (int)reader.ReadUInt32()
            : reader.ReadUInt16();

        string header = new(reader.ReadChars(headerLen));
        var (rows, cols) = ParseShape(header);

        var result = new float[rows, cols];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                result[r, c] = reader.ReadSingle();

        return result;
    }

    private static (int rows, int cols) ParseShape(string header)
    {
        int shapeStart = header.IndexOf("'shape'") + 7;
        int parenOpen = header.IndexOf('(', shapeStart);
        int parenClose = header.IndexOf(')', parenOpen);
        string shapeStr = header[(parenOpen + 1)..parenClose];

        int[] dims = [.. shapeStr
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)];

        return (dims[0], dims[1]);
    }
}