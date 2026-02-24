using System.Text.Json;

namespace Kitten.Voice;

/// <summary>
/// Tokenizes text for KittenTTS using the vocabulary from tokenizer.json.
/// </summary>
public class KokoroTokenizer
{
    private readonly Dictionary<string, int> _vocab;
    private readonly int _bosEosId;

    public int VocabSize => _vocab.Count;

    private KokoroTokenizer(Dictionary<string, int> vocab, int bosEosId)
    {
        _vocab = vocab;
        _bosEosId = bosEosId;
    }

    /// <summary>
    /// Loads the tokenizer from a tokenizer.json file.
    /// </summary>
    public static KokoroTokenizer Load(string path)
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
    public long[] Process(string text)
    {
        string phonemes = EnglishToIpa.Convert(text);
        return Tokenize(phonemes);
    }

    /// <summary>
    /// Tokenizes an IPA phoneme string into token IDs, wrapped with BOS/EOS tokens.
    /// </summary>
    public long[] Tokenize(string phonemes)
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