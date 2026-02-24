using System.Text.Json;
using Kitten.Voice.TextProcessing;

namespace Kitten.Voice.Tokenization;

/// <summary>
/// Tokenizes text for KittenTTS using the vocabulary from tokenizer.json.
/// </summary>
internal sealed class KokoroTokenizer
{
    private readonly Dictionary<string, int> _vocab;
    private readonly int _bosEosId;

    /// <summary>
    /// The size of the tokenizer's vocabulary, including special tokens.
    /// </summary>
    internal int VocabSize => _vocab.Count;

    private KokoroTokenizer(Dictionary<string, int> vocab, int bosEosId)
    {
        _vocab = vocab;
        _bosEosId = bosEosId;
    }

    /// <summary>
    /// Loads the tokenizer from a tokenizer.json file.
    /// </summary>
    /// <param name="path">The path to the tokenizer.json file.</param>
    /// <returns>A <see cref="KokoroTokenizer"/> instance.</returns>
    internal static KokoroTokenizer Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Tokenizer file not found", path);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        var vocabElement = doc.RootElement
            .GetProperty("model")
            .GetProperty("vocab");

        var vocab = new Dictionary<string, int>();
        foreach (var prop in vocabElement.EnumerateObject())
            vocab[prop.Name] = prop.Value.GetInt32();

        int bosEosId = vocab.GetValueOrDefault("$", 0);
        return new KokoroTokenizer(vocab, bosEosId);
    }

    /// <summary>
    /// Converts English text to IPA phonemes and tokenizes for the model.
    /// </summary>
    internal long[] Process(string text)
    {
        string phonemes = EnglishToIpa.Convert(text);
        return Tokenize(phonemes);
    }

    /// <summary>
    /// Tokenizes an IPA phoneme string into token IDs, wrapped with BOS/EOS tokens.
    /// </summary>
    internal long[] Tokenize(string phonemes)
    {
        var ids = new List<long> { _bosEosId };

        foreach (char c in phonemes)
        {
            if (_vocab.TryGetValue(c.ToString(), out int id))
                ids.Add(id);
        }

        ids.Add(_bosEosId);
        return [.. ids];
    }
}
