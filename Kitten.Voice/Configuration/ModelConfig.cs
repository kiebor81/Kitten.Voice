using System.Text.Json;

namespace Kitten.Voice.Configuration;

/// <summary>
/// Loads and resolves model configuration from config.json.
/// </summary>
internal sealed class ModelConfig
{
    /// <summary>
    /// Path to the model file (e.g. .bin or .gguf) relative to the assets directory.
    /// </summary>
    internal required string ModelPath { get; init; }
    /// <summary>
    /// Path to the voices directory relative to the assets directory. This directory should contain voice embedding files (e.g. .bin or .gguf) and a "voices.json" file with metadata.
    /// </summary>
    internal required string VoicesPath { get; init; }
    /// <summary>
    /// Path to the CMU pronunciation dictionary file relative to the assets directory.
    /// </summary>
    internal required string CmuDictPath { get; init; }
    /// <summary>
    /// Optional mapping of friendly voice names to internal embedding names. This allows users to refer to voices by more intuitive names in prompts. If a voice name is not found in this mapping, it will be used as-is when looking up the embedding file.
    /// </summary>
    internal Dictionary<string, string> VoiceAliases { get; init; } = [];
    /// <summary>
    /// Optional mapping of words or phrases to their desired phonetic pronunciations. This can be used to correct mispronunciations or specify pronunciations for uncommon words. The keys are the original words/phrases, and the values are the phonetic representations (e.g. using IPA or a custom phonetic notation). During synthesis, occurrences of the keys in the input text will be replaced with their corresponding phonetic values before being processed by the model.
    /// </summary>
    internal Dictionary<string, string> PronunciationOverrides { get; init; } = [];

    /// <summary>
    /// Loads model configuration from the specified assets directory.
    /// </summary>
    internal static ModelConfig Load(string assetsDir)
    {
        string configPath = Path.Combine(assetsDir, "config.json");
        if (!File.Exists(configPath))
            throw new FileNotFoundException("Config file not found", configPath);

        var root = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(configPath));

        string modelPath = Path.Combine(assetsDir, root.GetProperty("model_file").GetString()!);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("Model file not found", modelPath);

        string cmuDictPath = ResolveAssetPath(root, assetsDir, "cmu_dict_file", "cmudict.dict");
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("voice_aliases", out var aliasesElement))
        {
            foreach (var prop in aliasesElement.EnumerateObject())
                aliases[prop.Name] = prop.Value.GetString()!;
        }

        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("pronunciation_overrides", out var overridesElement)
            && overridesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in overridesElement.EnumerateObject())
            {
                string? value = prop.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    overrides[prop.Name] = value;
            }
        }

        return new ModelConfig
        {
            ModelPath = modelPath,
            VoicesPath = Path.Combine(assetsDir, root.GetProperty("voices").GetString()!),
            CmuDictPath = cmuDictPath,
            VoiceAliases = aliases,
            PronunciationOverrides = overrides,
        };
    }

    /// <summary>
    /// Resolves a friendly voice name to its internal embedding name.
    /// Returns the input unchanged if no alias is found.
    /// </summary>
    internal string ResolveVoice(string voiceName) =>
        VoiceAliases.TryGetValue(voiceName, out string? resolved) ? resolved : voiceName;

    private static string ResolveAssetPath(
        JsonElement root,
        string assetsDir,
        string propertyName,
        string fallbackFile)
    {
        if (root.TryGetProperty(propertyName, out JsonElement pathElement)
            && pathElement.ValueKind == JsonValueKind.String)
        {
            string? relativePath = pathElement.GetString();
            if (!string.IsNullOrWhiteSpace(relativePath))
                return Path.Combine(assetsDir, relativePath);
        }

        return Path.Combine(assetsDir, fallbackFile);
    }
}
