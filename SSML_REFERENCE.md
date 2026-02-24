# SSML Reference

This project supports a focused SSML subset for synthesis.

## Root

- `<speak>...</speak>`: Optional root wrapper.

## Timing

- `<break time="500ms"/>`
- `time` supports `ms` and `s` (example: `250ms`, `1.2s`).

## Voice Selection

- `<voice name="Bella">...</voice>`
- Uses aliases from `Kitten.Voice/assets/config.json`.

## Prosody

- `<prosody rate="..." volume="..." pitch="...">...</prosody>`

### `rate`

- Keywords: `x-slow`, `slow`, `medium`, `fast`, `x-fast`
- Percent: `70%`, `120%`

### `volume`

- Keywords: `silent`, `x-soft`, `soft`, `medium`, `loud`, `x-loud`
- Percent: `80%`, `140%`

### `pitch`

- Keywords: `x-low`, `low`, `medium`, `high`, `x-high`
- Semitones: `-2st`, `+3st`

## Emphasis

- `<emphasis>...</emphasis>`
- Applies slower + louder delivery.

## Say-As

- `<say-as interpret-as="spell-out">TTS</say-as>`
- Currently supports `interpret-as="spell-out"`.

## Emotion Extensions

- `<emotion name="happy" intensity="strong">...</emotion>`
- `<express-as style="calm" styledegree="1.2">...</express-as>`

### Emotion name/style

- `happy`, `excited`, `sad`, `angry`, `calm`, `fearful` (aliases are mapped internally).

### Emotion intensity

- Keywords: `x-weak`, `weak`, `medium`, `strong`, `x-strong`, `none`
- Numeric scalar: `0.8`, `1.2`
- Percent: `80%`, `130%`

## Single-line Example

```xml
<speak><voice name="Bella"><emotion name="happy" intensity="strong">Great news, the build is green.</emotion><break time="250ms"/><prosody rate="slow" pitch="-2st">Now we will walk through the details.</prosody></voice></speak>
```
