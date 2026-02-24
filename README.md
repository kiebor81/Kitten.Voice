# Kitten.Voice

`Kitten.Voice` is a .NET 9 text-to-speech library and test UI for [KittenTTS](https://github.com/KittenML) with SSML pre-parsing and waveform post-processing.

## What is included

- `Kitten.Voice`: Core synthesis library
- `Kitten.Voice.UI`: Avalonia desktop test app (dark mode by default)
- `SSML_REFERENCE.md`: focused SSML reference used by this project

## Features

- ONNX runtime inference (`Microsoft.ML.OnnxRuntime`)
- Voice aliases from `assets/config.json`
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
- Windows recommended for playback (NAudio-based output path)
- Model assets are not committed to this repository and must be downloaded manually.
- Required files in `Kitten.Voice/assets`:
  - `kitten_tts_mini_v0_8.onnx`
  - `voices.npz`
  - `tokenizer.json`
  - `config.json`
  - `cmudict.dict`

## Download required assets

Place these files in `Kitten.Voice/assets`:

- KittenTTS model files (`kitten_tts_mini_v0_8.onnx`, `tokenizer.json`, `config.json`):
  - https://huggingface.co/KittenML
- Voice embeddings (`voices.npz`) from KittenTTS repositories on Hugging Face:
  - https://huggingface.co/KittenML
- CMU Pronouncing Dictionary (`cmudict.dict`):
  - https://github.com/cmusphinx/cmudict

## config.json role

`Kitten.Voice/assets/config.json` is used at runtime to control:

- Which ONNX model file is loaded (`model_file`)
- Which voice embeddings file is loaded (`voices`)
- Friendly voice name aliases (`voice_aliases`)

## Project structure

```text
/
  README.md
  SSML_REFERENCE.md
  Kitten.Voice/
    Kitten.Voice.sln
    Kitten.Voice.csproj
    Speaker.cs
    SsmlParser.cs
    WaveformProcessor.cs
    assets/
  Kitten.Voice.UI/
    Kitten.Voice.UI.csproj
    MainWindow.axaml
```

## Build

From repository root:

```powershell
dotnet build Kitten.Voice/Kitten.Voice.sln
```

## Run the UI

```powershell
dotnet run --project Kitten.Voice.UI/Kitten.Voice.UI.csproj
```

UI supports:

- Plain text input with voice picker and `Send To Play`
- SSML builder with emotion/prosody controls
- SSML preview and `Send To Play`

## Use the library in code

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

### Output modes

- `AudioOutput.Stream`: play directly from memory
- `AudioOutput.File`: save WAV then play
- `AudioOutput.FileOnly`: save WAV only

## Supported voices (default config based on KittenTTS voices.npz)

- `Bella`
- `Jasper`
- `Luna`
- `Bruno`
- `Rosie`
- `Hugo`
- `Kiki`
- `Leo`

Voice aliases are configured in:

- `Kitten.Voice/assets/config.json`

## SSML quick examples

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

For complete tag details, see:

- `SSML_REFERENCE.md`

SSML Parsing is experimental and far from a complete implementation. For full SSML capable models, cloud hosted options exist.

## Troubleshooting

- `Config file not found` / `Model file not found`:
  - Verify `assets` path passed to `Speaker(...)`
- No audio:
  - Check output device and output mode (`Stream` vs `File`)
- Distortion with imported voices:
  - Revert to known-compatible voices
  - Confirm voice embeddings match the model family
- SSML not parsed:
  - Input must start with `<` or be wrapped by `<speak>...</speak>`

## Notes for development

- Solution path: `Kitten.Voice/Kitten.Voice.sln`
- Main synthesis entry point: `Kitten.Voice/Speaker.cs`
- SSML parser: `Kitten.Voice/SsmlParser.cs`
