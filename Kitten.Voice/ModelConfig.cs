using System.Text.Json;

namespace Kitten.Voice;

/// <summary>
/// Loads and resolves model configuration from config.json.
/// </summary>
public class ModelConfig
{
    public required string ModelPath { get; init; }
    public required string VoicesPath { get; init; }
    public Dictionary<string, string> VoiceAliases { get; init; } = [];

    /// <summary>
    /// Loads model configuration from the specified assets directory.
    /// </summary>
    public static ModelConfig Load(string assetsDir)
    {
        string configPath = Path.Combine(assetsDir, "config.json");
        if (!File.Exists(configPath))
            throw new FileNotFoundException("Config file not found", configPath);

        var root = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(configPath));

        string modelPath = Path.Combine(assetsDir, root.GetProperty("model_file").GetString()!);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Model file not found", modelPath);

        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("voice_aliases", out var aliasesElement))
        {
            foreach (var prop in aliasesElement.EnumerateObject())
                aliases[prop.Name] = prop.Value.GetString()!;
        }

        return new ModelConfig
        {
            ModelPath = modelPath,
            VoicesPath = Path.Combine(assetsDir, root.GetProperty("voices").GetString()!),
            VoiceAliases = aliases,
        };
    }

    /// <summary>
    /// Resolves a friendly voice name to its internal embedding name.
    /// Returns the input unchanged if no alias is found.
    /// </summary>
    public string ResolveVoice(string voiceName) =>
        VoiceAliases.TryGetValue(voiceName, out string? resolved) ? resolved : voiceName;
}