# Audio Gain Boost (mic + speaker, up to 200%) — Design

**Date:** 2026-07-12
**Component:** OrbitalSIP (.NET / Avalonia softphone)
**Status:** Approved (design), pending implementation plan

## 1. Goal

Let the operator boost outgoing microphone level and incoming speaker level
independently, during an active call, via presets:

- **Microphone:** 50, 100, 150, 200 (%)
- **Speaker:** 0, 50, 100, 150, 200 (%)

100 % = unity (no change). Values above 100 % apply real digital gain. A soft
limiter prevents harsh clipping when boosting. Selected levels are saved and
become the default for subsequent calls.

## 2. Background / Constraints

- Media stack: `SIPSorcery` 8.0.0 + `SIPSorceryMedia.Windows` 8.0.7.
- Audio runs through `WindowsAudioEndPoint` (NAudio `WaveInEvent` capture →
  `AudioEncoder.EncodeAudio` → RTP; RTP → `DecodeAudio` → `BufferedWaveProvider`
  → `WaveOutEvent` playback).
- Verified (string dump of the shipped DLL): `WindowsAudioEndPoint` exposes **no
  gain/volume member**. Mute is implemented via `PauseAudio()`.
- OS-level volume (NAudio WASAPI `AudioEndpointVolume`) caps the scalar at 1.0,
  so it **cannot** reach 200 %. Rejected.
- The only place raw PCM exists in **both** directions is inside the endpoint,
  between capture and encode (TX) and between decode and playback (RX). That hook
  is not exposed publicly → we fork the endpoint.

## 3. Chosen Approach

Fork `WindowsAudioEndPoint` (SIPSorcery is MIT-licensed) into a project-local
`GainAudioEndPoint`, and multiply the PCM buffer by a per-direction gain factor
immediately before encode (mic) and immediately before playback (speaker),
passing each buffer through a stateless soft limiter.

Alternatives considered and rejected:

- **Custom `IAudioSource`/`IAudioSink` from scratch** — reimplements device
  enumeration and format negotiation already solved by the endpoint; larger bug
  surface.
- **WASAPI endpoint volume** — capped at 100 %; cannot satisfy the 200 %
  requirement.

## 4. Components

Each unit has one purpose, a clear interface, and is independently testable.

### 4.1 `AudioGain` — pure DSP helper (`OrbitalSIP/Services/Audio/AudioGain.cs`)

```
static void Apply(Span<short> pcm, float gain)
```

- No-op fast path when `gain == 1.0f`.
- Per sample: normalize `s = gain * sample / 32768f`, apply soft limiter, write
  back as `short` (rounded, clamped to Int16 range as a final safety net).
- **Soft limiter** (stateless, cheap, linear below threshold):
  - `T = 0.8f`
  - `|s| <= T` → `s`
  - else → `sign(s) * (T + (1 - T) * tanh((|s| - T) / (1 - T)))`
  - Asymptotes to ±1.0; C¹-continuous at the knee; no wrap/overflow.
- Pure and allocation-free → unit-testable in isolation.

### 4.2 `GainAudioEndPoint` — forked endpoint (`OrbitalSIP/Services/Audio/GainAudioEndPoint.cs`)

- Copied from the `SIPSorceryMedia.Windows` 8.0.7 source
  (`WindowsAudioEndPoint.cs`, MIT), renamed, same constructor signature
  `(AudioEncoder, int audioOutDeviceIndex, int audioInDeviceIndex, ...)` and same
  public surface used today (`RestrictFormats`, `GetAudioSinkFormats`,
  `ToMediaEndPoints`, `PauseAudio`, `OnAudioSourceError`, …).
- Adds two public fields: `float SourceGain = 1f;` (mic/TX) and
  `float SinkGain = 1f;` (speaker/RX). Single-float writes are atomic in .NET, so
  the UI thread can set them while the audio thread reads them without tearing.
- TX: call `AudioGain.Apply(pcmBuffer, SourceGain)` on the captured PCM before
  `EncodeAudio`.
- RX: call `AudioGain.Apply(pcmBuffer, SinkGain)` on the decoded PCM before
  `BufferedWaveProvider.AddSamples`.

### 4.3 `SipService` (`OrbitalSIP/Services/SipService.cs`)

- `TryCreateAudio()` constructs `GainAudioEndPoint` instead of
  `WindowsAudioEndPoint`, then applies saved levels:
  `_audioEndPoint.SourceGain = _settings.MicGainPercent / 100f;`
  `_audioEndPoint.SinkGain = _settings.SpeakerGainPercent / 100f;`
- New public methods (callable during an active call, effect is immediate):
  - `void SetMicGain(int percent)` — clamp to [50, 200], set
    `_audioEndPoint.SourceGain` if present, store `_settings.MicGainPercent`,
    `_settings.Save()`.
  - `void SetSpeakerGain(int percent)` — clamp to [0, 200], set
    `_audioEndPoint.SinkGain` if present, store `_settings.SpeakerGainPercent`,
    `_settings.Save()`.
- Mute is unchanged (still `PauseAudio()`), independent of gain.

### 4.4 `SipSettings` (`OrbitalSIP/Services/SipSettings.cs`)

```
public int MicGainPercent     { get; set; } = 100;  // 50..200
public int SpeakerGainPercent { get; set; } = 100;  // 0..200
```

Serialized to `sip-settings.json` (not `[JsonIgnore]`). Persistence makes the
last-chosen levels the default across calls.

### 4.5 `ActiveCallView` (`OrbitalSIP/Views/ActiveCallView.axaml` + `.axaml.cs`)

- Two preset rows (segmented buttons, styled like the existing `MuteBtn`:
  bg `#1A2D42`, fg `#DDE7F3`, selected accent):
  - 🎙 Микрофон: `50 · 100 · 150 · 200`
  - 🔊 Звук: `0 · 50 · 100 · 150 · 200`
- Selected preset is highlighted; changing it raises
  `event EventHandler<int>? OnMicGainChanged` / `OnSpeakerGainChanged`.
- Initial selection is read from `SipSettings` when the view is shown.
- Presets live in the expanded `ActiveCallView` only — the compact
  `ActiveCallWidgetView` (FAB) has no room and is out of scope.

### 4.6 Wiring (`OrbitalSIP/MainWindow.axaml.cs`)

In `WireActiveCallView(callView)`, alongside the existing mute/hold wiring:

```
callView.OnMicGainChanged     += (_, pct) => App.SipService.SetMicGain(pct);
callView.OnSpeakerGainChanged += (_, pct) => App.SipService.SetSpeakerGain(pct);
```

## 5. Data Flow

- **TX (mic):** `WaveInEvent` → PCM → `AudioGain.Apply(pcm, SourceGain)` →
  `EncodeAudio` → RTP.
- **RX (speaker):** RTP → `DecodeAudio` → `AudioGain.Apply(pcm, SinkGain)` →
  `BufferedWaveProvider.AddSamples` → `WaveOutEvent`.

## 6. Error Handling & Edge Cases

- Gain factors default to 1.0 (unity) so absent/invalid settings are harmless.
- Setters clamp to the allowed range before applying and saving.
- Gain lives on the per-call endpoint instance; settings persist independently,
  so a change mid-call both takes effect immediately and survives to the next call.
- Mic muting is exclusively the Mute button; there is no 0 % mic preset.
- Speaker 0 % is valid silence (distinct from mute; nothing is muted at the mic).
- `settings.Save()` is wrapped by its existing try/catch-on-load contract; a write
  failure must not crash a live call (guard if needed).

## 7. Testing

No .NET test project exists yet (solution contains only `OrbitalSIP`). Create a
minimal `OrbitalSIP.Tests` xUnit project targeting `net8.0-windows10.0.17763`,
referencing `OrbitalSIP`, and add it to `vv-phone-widget.sln`. Only `AudioGain`
(pure, no device I/O) is unit-tested; `GainAudioEndPoint` is validated manually.

- **Unit (`AudioGain.Apply`):**
  - gain 1.0, low-level input → output equals input (within ±1 LSB rounding).
  - gain 2.0 on full-scale input → `|output| <= 32767`, monotonic vs input, sign
    preserved, no wrap/overflow.
  - sweep of gains {0, 0.5, 1.5, 2.0} → outputs bounded, no exceptions.
- **Manual:** place a call, change mic and speaker presets live, confirm audible
  level change with no crackle/wrap; confirm chosen levels persist to a new call;
  confirm Mute still works independently.

## 8. Out of Scope

- Gain controls in the compact FAB widget.
- Per-contact / per-line gain profiles.
- Look-ahead / attack-release compressor (stateless soft clip is sufficient).
- Automatic gain control (AGC).

## 9. Files Touched

- `OrbitalSIP/Services/Audio/AudioGain.cs` (new)
- `OrbitalSIP/Services/Audio/GainAudioEndPoint.cs` (new, forked)
- `OrbitalSIP/Services/SipService.cs` (edit)
- `OrbitalSIP/Services/SipSettings.cs` (edit)
- `OrbitalSIP/Views/ActiveCallView.axaml` + `.axaml.cs` (edit)
- `OrbitalSIP/MainWindow.axaml.cs` (edit)
- `OrbitalSIP.Tests/` xUnit project (new) + `vv-phone-widget.sln` (edit) — unit tests for `AudioGain`
