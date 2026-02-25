# Kitten.Voice

`Kitten.Voice` is a .NET 9 text-to-speech library and test UI for [KittenTTS](https://github.com/KittenML), with SSML parsing and waveform post-processing.

## What Is Included

- `Kitten.Voice`: core synthesis library
- `Kitten.Voice.UI`: Avalonia desktop test app
- `SSML_REFERENCE.md`: focused SSML reference used by this project

## Features

- ONNX runtime inference (`Microsoft.ML.OnnxRuntime`)
- Reused ONNX inference sessions per model path (lower per-call overhead)
- Cached voice embeddings loaded from `voices.npz`
- Voice aliases from `assets/config.json`
- Pronunciation overrides from `assets/config.json` (`pronunciation_overrides`)
- Configurable CMU dictionary path from `assets/config.json` (`cmu_dict_file`)
- Plain-text pause cues for:
  - newline
  - ellipsis (`...` and `…`)
  - em dash (`—`)
  - comma, semicolon, colon
  - period, question mark, exclamation mark
- Heuristic punctuation inflection for plain text segment tails (`?` and `!`)
- SSML parsing for:
  - `speak`, `break`, `prosody`, `emphasis`, `voice`, `say-as`
  - custom emotion extensions: `emotion`, `express-as`
- Per-segment post-processing:
  - volume, pitch shift, silence insertion
  - soft clipping and peak limiting for distortion control
- Simple UI for fast iteration:
  - plain text -> play
  - SSML builder -> generate -> play

## Requirements

- .NET SDK 9.0+
- Windows recommended for playback (NAudio output path)
- Model assets are not committed to this repository and must be downloaded manually.
- Required files in `Kitten.Voice/assets`:
  - `kitten_tts_mini_v0_8.onnx`
  - `voices.npz`
  - `tokenizer.json`
  - `config.json`
  - `cmudict.dict`

## Download Required Assets

Place these files in `Kitten.Voice/assets`:

- KittenTTS model files (`kitten_tts_mini_v0_8.onnx`, `tokenizer.json`, `config.json`):
  - https://huggingface.co/KittenML
- Voice embeddings (`voices.npz`) from KittenTTS repositories on Hugging Face:
  - https://huggingface.co/KittenML
- CMU Pronouncing Dictionary (`cmudict.dict`):
  - https://github.com/cmusphinx/cmudict

## `config.json` Role

`Kitten.Voice/assets/config.json` is used at runtime to control:

- Which ONNX model file is loaded (`model_file`)
- Which voice embeddings file is loaded (`voices`)
- Which CMU dictionary file is loaded (`cmu_dict_file`)
- Friendly voice name aliases (`voice_aliases`)
- Per-word ARPAbet pronunciation overrides (`pronunciation_overrides`)

Example:

```json
"pronunciation_overrides": {
  "SQL": "EH1 S K Y UW1 EH1 L",
  "Kubernetes": "K UW2 B ER0 N EH1 T IY0 Z"
}
```

## Project Structure

```text
Kitten.Voice/
  Speaker.cs
  Audio/
  Configuration/
  Embeddings/
  Ssml/
  TextProcessing/
  Tokenization/
  assets/
Kitten.Voice.UI/
  Kitten.Voice.UI.csproj
  MainWindow.axaml
```

## Build

From repository root:

```powershell
dotnet build Kitten.Voice.sln
```

## Run the UI

```powershell
dotnet run --project Kitten.Voice.UI/Kitten.Voice.UI.csproj
```

UI supports:

- Plain text input with voice picker
- SSML builder with emotion/prosody controls
- SSML preview

## Use the Library in Code

```csharp
using Kitten.Voice;

var speaker = new Speaker("Kitten.Voice/assets")
{
    Voice = "Bella",
    Speed = 1.2f,
    Expressiveness = 1.0f,
    Output = AudioOutput.Stream
};

speaker.Say("Hello from Kitten Voice.");
```

### Output Modes

- `AudioOutput.Stream`: play directly from memory
- `AudioOutput.File`: save WAV then play
- `AudioOutput.FileOnly`: save WAV only

## Supported Voices

Default aliases in `config.json`:

- `Bella`
- `Jasper`
- `Luna`
- `Bruno`
- `Rosie`
- `Hugo`
- `Kiki`
- `Leo`

## SSML Quick Examples

Plain SSML:

```xml
<speak><voice name="Bella">Hello from SSML.</voice></speak>
```

Emotion + prosody:

```xml
<speak>
  <voice name="Luna">
    <emotion name="happy" intensity="strong">Great news, everything passed.</emotion>
    <break time="250ms"/>
    <prosody rate="slow" pitch="-2st">Now we will walk through details.</prosody>
  </voice>
</speak>
```

For complete tag details, see `SSML_REFERENCE.md`.

SSML parsing is experimental and is not a complete SSML implementation.

## Troubleshooting

- `Config file not found` / `Model file not found`:
  - Verify the `assets` path passed to `Speaker(...)`
- No audio:
  - Check output device and output mode (`Stream` vs `File`)
- Distortion with imported voices:
  - Revert to known-compatible voices
  - Confirm voice embeddings match the model family
- SSML not parsed:
  - Input must start with `<` or be wrapped by `<speak>...</speak>`

## Notes for Development

- Solution path: `Kitten.Voice.sln`
- Main synthesis entry point: `Kitten.Voice/Speaker.cs`
- SSML parser: `Kitten.Voice/Ssml/SsmlParser.cs`
