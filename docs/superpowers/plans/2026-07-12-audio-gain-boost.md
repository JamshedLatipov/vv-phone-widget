# Audio Gain Boost Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the operator boost microphone (50–200 %) and speaker (0–200 %) levels independently during a call via presets, persisted as defaults, with a stateless soft limiter to avoid harsh clipping.

**Architecture:** Fork `WindowsAudioEndPoint` (SIPSorceryMedia.Windows 8.0.7, MIT) into a project-local `GainAudioEndPoint` and multiply the raw PCM buffer by a per-direction gain factor at the only two points raw PCM exists: mic capture (before encode) and RTP decode (before playback). A pure `AudioGain` helper does the gain + soft-clip math and is unit-tested. Presets live in the expanded `ActiveCallView`, wired through `SipService`, and saved to `sip-settings.json`.

**Tech Stack:** .NET 8 (`net8.0-windows10.0.17763`), Avalonia UI, SIPSorcery 8.0.0 / SIPSorceryMedia.Windows 8.0.7, NAudio 2.2.1, xUnit (new test project).

**Spec:** `docs/superpowers/specs/2026-07-12-audio-gain-boost-design.md`

---

## File Structure

| File | Responsibility |
|------|----------------|
| `OrbitalSIP/Services/Audio/AudioGain.cs` (new) | Pure DSP: per-sample gain + soft limiter. Testable. |
| `OrbitalSIP/Services/Audio/GainAudioEndPoint.cs` (new, forked) | Device I/O + injects `AudioGain.Apply` in TX and RX. Holds `SourceGain`/`SinkGain`. |
| `OrbitalSIP/Services/SipSettings.cs` (edit) | Persist `MicGainPercent`, `SpeakerGainPercent`. |
| `OrbitalSIP/Services/SipService.cs` (edit) | Use `GainAudioEndPoint`; apply saved gains on call; `SetMicGain`/`SetSpeakerGain` + getters. |
| `OrbitalSIP/Views/ActiveCallView.axaml` (edit) | Two preset rows (mic + speaker). |
| `OrbitalSIP/Views/ActiveCallView.axaml.cs` (edit) | Wire presets, highlight selection, raise events, init from settings. |
| `OrbitalSIP/MainWindow.axaml.cs` (edit) | Subscribe preset events → `SipService`. |
| `OrbitalSIP.Tests/` (new project) | xUnit tests for `AudioGain`. |

---

## Task 1: Scaffold the test project

**Files:**
- Create: `OrbitalSIP.Tests/OrbitalSIP.Tests.csproj`
- Create: `OrbitalSIP.Tests/AudioGainTests.cs` (placeholder in this task)
- Modify: `vv-phone-widget.sln`

- [ ] **Step 1: Create the test project**

Run:
```
dotnet new xunit -o OrbitalSIP.Tests -f net8.0
```

- [ ] **Step 2: Set the TFM and reference the app project**

Overwrite `OrbitalSIP.Tests/OrbitalSIP.Tests.csproj` with:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.17763</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\OrbitalSIP\OrbitalSIP.csproj" />
  </ItemGroup>

</Project>
```
(If `dotnet new` pinned different package versions, keep the versions it generated — only the `TargetFramework` and the `ProjectReference` matter here.)

- [ ] **Step 3: Add the test project to the solution**

Run:
```
dotnet sln vv-phone-widget.sln add OrbitalSIP.Tests/OrbitalSIP.Tests.csproj
```

- [ ] **Step 4: Verify the empty test project builds and runs**

Run:
```
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj
```
Expected: build succeeds; the default `UnitTest1` (or zero tests after you delete it) passes. Delete the generated `OrbitalSIP.Tests/UnitTest1.cs` if present.

- [ ] **Step 5: Commit**

```bash
git add OrbitalSIP.Tests vv-phone-widget.sln
git commit -m "test: scaffold OrbitalSIP.Tests xUnit project"
```

---

## Task 2: `AudioGain` pure DSP (TDD)

**Files:**
- Create: `OrbitalSIP/Services/Audio/AudioGain.cs`
- Test: `OrbitalSIP.Tests/AudioGainTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `OrbitalSIP.Tests/AudioGainTests.cs`:
```csharp
using OrbitalSIP.Services.Audio;
using Xunit;

namespace OrbitalSIP.Tests
{
    public class AudioGainTests
    {
        [Fact]
        public void Unity_LeavesSamplesUnchanged()
        {
            short[] pcm = { 0, 100, -200, 32767, -32768 };
            short[] expected = (short[])pcm.Clone();
            AudioGain.Apply(pcm, 1.0f);
            Assert.Equal(expected, pcm);
        }

        [Fact]
        public void Attenuation_HalvesLowLevelSample()
        {
            short[] pcm = { 1000, -1000 };
            AudioGain.Apply(pcm, 0.5f);
            Assert.InRange(pcm[0], 499, 501);
            Assert.InRange(pcm[1], -501, -499);
        }

        [Fact]
        public void ZeroGain_SilencesEverything()
        {
            short[] pcm = { 1000, -1000, 32767, -32768 };
            AudioGain.Apply(pcm, 0.0f);
            Assert.All(pcm, s => Assert.Equal(0, s));
        }

        [Fact]
        public void Boost_NeverExceedsInt16Range()
        {
            short[] pcm = { 30000, -30000, 32767, -32768, 16000, -16000 };
            AudioGain.Apply(pcm, 2.0f);
            Assert.All(pcm, s => Assert.InRange(s, short.MinValue, short.MaxValue));
        }

        [Fact]
        public void Boost_PreservesSign()
        {
            short[] pcm = { 30000, -30000 };
            AudioGain.Apply(pcm, 2.0f);
            Assert.True(pcm[0] > 0);
            Assert.True(pcm[1] < 0);
        }

        [Fact]
        public void Boost_IsMonotonic()
        {
            short[] a = { 1000 };
            short[] b = { 2000 };
            AudioGain.Apply(a, 2.0f);
            AudioGain.Apply(b, 2.0f);
            Assert.True(b[0] >= a[0]);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:
```
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj
```
Expected: FAIL to compile — `AudioGain` does not exist (`CS0246`/`CS0103`).

- [ ] **Step 3: Implement `AudioGain`**

Create `OrbitalSIP/Services/Audio/AudioGain.cs`:
```csharp
using System;

namespace OrbitalSIP.Services.Audio
{
    /// <summary>
    /// Stateless per-sample digital gain with a soft limiter.
    /// gain 1.0 = unity passthrough. Below the knee (T) the response is linear;
    /// above it a tanh soft-clip smoothly asymptotes to full scale, so the output
    /// never wraps or overflows Int16 no matter how high the gain.
    /// </summary>
    public static class AudioGain
    {
        private const float T = 0.8f;          // soft-knee threshold, fraction of full scale
        private const float FullScale = 32768f;

        /// <summary>Amplify PCM in place. gain: 0.0 = silence, 1.0 = unity, 2.0 = +6 dB.</summary>
        public static void Apply(Span<short> pcm, float gain)
        {
            if (gain == 1.0f) return;          // fast path: unity is a no-op (no coloring)

            for (int i = 0; i < pcm.Length; i++)
            {
                float s = gain * (pcm[i] / FullScale);   // normalize to [-1,1] and apply gain
                float y = SoftClip(s);
                int v = (int)MathF.Round(y * FullScale);
                if (v > short.MaxValue) v = short.MaxValue;
                else if (v < short.MinValue) v = short.MinValue;
                pcm[i] = (short)v;
            }
        }

        private static float SoftClip(float s)
        {
            float a = MathF.Abs(s);
            if (a <= T) return s;                          // linear region
            float sign = s < 0 ? -1f : 1f;
            float over = (a - T) / (1f - T);
            return sign * (T + (1f - T) * MathF.Tanh(over)); // smooth knee, ->±1.0
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:
```
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj
```
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add OrbitalSIP/Services/Audio/AudioGain.cs OrbitalSIP.Tests/AudioGainTests.cs
git commit -m "feat(audio): AudioGain per-sample gain with soft limiter"
```

---

## Task 3: Persist gain levels in `SipSettings`

**Files:**
- Modify: `OrbitalSIP/Services/SipSettings.cs`

- [ ] **Step 1: Add the two persisted fields**

In `OrbitalSIP/Services/SipSettings.cs`, immediately after the `AudioInDeviceIndex` property (around line 32), add:
```csharp
        /// <summary>Outgoing mic gain as a percent. 50..200. 100 = unity.</summary>
        public int MicGainPercent { get; set; } = 100;
        /// <summary>Incoming speaker gain as a percent. 0..200. 100 = unity.</summary>
        public int SpeakerGainPercent { get; set; } = 100;
```

- [ ] **Step 2: Verify the app still builds**

Run:
```
dotnet build OrbitalSIP/OrbitalSIP.csproj -c Debug
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add OrbitalSIP/Services/SipSettings.cs
git commit -m "feat(settings): persist mic and speaker gain percents"
```

---

## Task 4: Fork the endpoint into `GainAudioEndPoint`

The stock `WindowsAudioEndPoint` exposes no PCM hook, so we copy it verbatim and make exactly three kinds of change: rename (namespace + class), add two gain fields, and inject one `AudioGain.Apply` call in each direction.

**Files:**
- Create: `OrbitalSIP/Services/Audio/GainAudioEndPoint.cs`

- [ ] **Step 1: Download the 8.0.7 source into place**

Run (PowerShell):
```powershell
New-Item -ItemType Directory -Force OrbitalSIP/Services/Audio | Out-Null
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/sipsorcery-org/SIPSorceryMedia.Windows/v8.0.7/src/WindowsAudioEndPoint.cs" -OutFile "OrbitalSIP/Services/Audio/GainAudioEndPoint.cs"
```
(Bash alternative: `curl -L -o OrbitalSIP/Services/Audio/GainAudioEndPoint.cs https://raw.githubusercontent.com/sipsorcery-org/SIPSorceryMedia.Windows/v8.0.7/src/WindowsAudioEndPoint.cs`)

Confirm the file downloaded and contains `class WindowsAudioEndPoint`, a capture handler that calls `EncodeAudio`, and a `GotAudioRtp` method that calls `DecodeAudio`.

- [ ] **Step 2: Rename namespace and class**

In `OrbitalSIP/Services/Audio/GainAudioEndPoint.cs`:
1. Change the namespace declaration from `namespace SIPSorceryMedia.Windows` to `namespace OrbitalSIP.Services.Audio`.
2. Rename every occurrence of the type `WindowsAudioEndPoint` to `GainAudioEndPoint` — the class declaration and both constructors (constructors must match the new class name or the file will not compile).
3. Add `using OrbitalSIP.Services.Audio;` is NOT needed (same namespace). Leave all original `using` directives intact.

- [ ] **Step 3: Add the two gain fields**

Inside the `GainAudioEndPoint` class body, near the other private fields, add these public fields:
```csharp
        /// <summary>Outgoing (mic) gain factor. 1.0 = unity. Set from the UI thread; read on the audio thread (single-float writes are atomic).</summary>
        public float SourceGain = 1f;
        /// <summary>Incoming (speaker) gain factor. 1.0 = unity.</summary>
        public float SinkGain = 1f;
```

- [ ] **Step 4: Inject mic gain (TX) before encode**

Find the capture callback (the method whose body builds a `short[]` PCM array from the `WaveInEventArgs` buffer and passes it to `_audioEncoder.EncodeAudio(...)` — in the 8.0.7 source this is `LocalAudioSampleAvailable`). Immediately **before** the `EncodeAudio` call, insert a gain call on that PCM array. Given the source shape:
```csharp
            // ... builds:  short[] pcm = ... ;
            AudioGain.Apply(pcm, SourceGain);            // <-- INSERT THIS LINE
            byte[] encodedSample = _audioEncoder.EncodeAudio(pcm, _audioFormatManager.SelectedFormat);
```
Use whatever local-variable name the real source gives the `short[]` that is passed to `EncodeAudio`.

- [ ] **Step 5: Inject speaker gain (RX) after decode**

Find `GotAudioRtp`. It calls `_audioEncoder.DecodeAudio(payload, ...)` returning a `short[]`, then converts it to bytes and calls `_waveProvider.AddSamples(...)`. Immediately **after** the decode, insert a gain call on the decoded `short[]`:
```csharp
            var pcmSample = _audioEncoder.DecodeAudio(payload, _audioFormatManager.SelectedFormat);
            AudioGain.Apply(pcmSample, SinkGain);        // <-- INSERT THIS LINE
            byte[] pcmBytes = pcmSample.SelectMany(x => BitConverter.GetBytes(x)).ToArray();
            _waveProvider?.AddSamples(pcmBytes, 0, pcmBytes.Length);
```
Use whatever local-variable name the real source gives the decoded `short[]`.

- [ ] **Step 6: Verify it builds**

Run:
```
dotnet build OrbitalSIP/OrbitalSIP.csproj -c Debug
```
Expected: Build succeeded. (Nullable/style warnings from the copied file are acceptable — they are warnings, not errors. If the compiler reports the constructor name does not match the class, you missed a `WindowsAudioEndPoint` occurrence in Step 2.)

- [ ] **Step 7: Commit**

```bash
git add OrbitalSIP/Services/Audio/GainAudioEndPoint.cs
git commit -m "feat(audio): fork WindowsAudioEndPoint as GainAudioEndPoint with PCM gain hooks"
```

---

## Task 5: Wire gain into `SipService`

**Files:**
- Modify: `OrbitalSIP/Services/SipService.cs`

- [ ] **Step 1: Add the namespace import**

At the top of `OrbitalSIP/Services/SipService.cs`, with the other `using` directives, add:
```csharp
using OrbitalSIP.Services.Audio;
```

- [ ] **Step 2: Change the endpoint field type**

Change the field declaration (around line 30) from:
```csharp
        private WindowsAudioEndPoint?        _audioEndPoint;
```
to:
```csharp
        private GainAudioEndPoint?           _audioEndPoint;
```

- [ ] **Step 3: Construct the forked endpoint and apply saved gains**

In `TryCreateAudio()`, change the constructor (around line 562) from `new WindowsAudioEndPoint(` to `new GainAudioEndPoint(`. Then, immediately after the `_audioEndPoint.OnAudioSourceError += ...` block (before `RestrictFormats`), insert:
```csharp
                _audioEndPoint.SourceGain = _settings.MicGainPercent / 100f;
                _audioEndPoint.SinkGain   = _settings.SpeakerGainPercent / 100f;
                Log($"Applied gains. mic={_settings.MicGainPercent}% speaker={_settings.SpeakerGainPercent}%");
```

- [ ] **Step 4: Add live setters and getters**

Immediately after the `SetMuted` method (around line 489), add:
```csharp
        public int MicGainPercent     => _settings.MicGainPercent;
        public int SpeakerGainPercent => _settings.SpeakerGainPercent;

        public void SetMicGain(int percent)
        {
            percent = Math.Clamp(percent, 50, 200);
            _settings.MicGainPercent = percent;
            if (_audioEndPoint != null) _audioEndPoint.SourceGain = percent / 100f;
            try { _settings.Save(); } catch (Exception ex) { Log($"SetMicGain save failed: {ex.Message}"); }
            Log($"Mic gain set to {percent}%");
        }

        public void SetSpeakerGain(int percent)
        {
            percent = Math.Clamp(percent, 0, 200);
            _settings.SpeakerGainPercent = percent;
            if (_audioEndPoint != null) _audioEndPoint.SinkGain = percent / 100f;
            try { _settings.Save(); } catch (Exception ex) { Log($"SetSpeakerGain save failed: {ex.Message}"); }
            Log($"Speaker gain set to {percent}%");
        }
```
(If `_settings` is named differently in this file, use that name. Confirm by checking the existing `_settings.AudioOutDeviceIndex` usage in `TryCreateAudio`.)

- [ ] **Step 5: Verify it builds**

Run:
```
dotnet build OrbitalSIP/OrbitalSIP.csproj -c Debug
```
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add OrbitalSIP/Services/SipService.cs
git commit -m "feat(audio): apply and expose mic/speaker gain in SipService"
```

---

## Task 6: Preset UI in `ActiveCallView`

**Files:**
- Modify: `OrbitalSIP/Views/ActiveCallView.axaml`
- Modify: `OrbitalSIP/Views/ActiveCallView.axaml.cs`

- [ ] **Step 1: Add the preset rows to the XAML**

In `OrbitalSIP/Views/ActiveCallView.axaml`, insert the following block **after** the closing `</UniformGrid>` of the mute/hold/keypad/transfer controls (after line 114) and **before** the `<Border Name="TransferPanel" ...>` (line 116):
```xml
          <!-- ── Volume presets (mic + speaker) ─────────────────── -->
          <StackPanel Spacing="8" Margin="0,2,0,0">
            <StackPanel Orientation="Horizontal" Spacing="10" HorizontalAlignment="Center">
              <materialIcons:MaterialIcon Kind="Microphone" Width="16" Height="16" Foreground="#8FA6BE" VerticalAlignment="Center" />
              <UniformGrid Columns="4" Rows="1" Width="238">
                <Button Name="MicGain50"  Margin="2" Height="30" CornerRadius="8" Background="#1E293B" BorderThickness="0" Padding="0" HorizontalContentAlignment="Center" VerticalContentAlignment="Center">
                  <TextBlock Text="50%" FontSize="11" FontWeight="Bold" Foreground="#8FA6BE" />
                </Button>
                <Button Name="MicGain100" Margin="2" Height="30" CornerRadius="8" Background="#1E293B" BorderThickness="0" Padding="0" HorizontalContentAlignment="Center" VerticalContentAlignment="Center">
                  <TextBlock Text="100%" FontSize="11" FontWeight="Bold" Foreground="#8FA6BE" />
                </Button>
                <Button Name="MicGain150" Margin="2" Height="30" CornerRadius="8" Background="#1E293B" BorderThickness="0" Padding="0" HorizontalContentAlignment="Center" VerticalContentAlignment="Center">
                  <TextBlock Text="150%" FontSize="11" FontWeight="Bold" Foreground="#8FA6BE" />
                </Button>
                <Button Name="MicGain200" Margin="2" Height="30" CornerRadius="8" Background="#1E293B" BorderThickness="0" Padding="0" HorizontalContentAlignment="Center" VerticalContentAlignment="Center">
                  <TextBlock Text="200%" FontSize="11" FontWeight="Bold" Foreground="#8FA6BE" />
                </Button>
              </UniformGrid>
            </StackPanel>
            <StackPanel Orientation="Horizontal" Spacing="10" HorizontalAlignment="Center">
              <materialIcons:MaterialIcon Kind="VolumeHigh" Width="16" Height="16" Foreground="#8FA6BE" VerticalAlignment="Center" />
              <UniformGrid Columns="5" Rows="1" Width="238">
                <Button Name="SpkGain0"   Margin="2" Height="30" CornerRadius="8" Background="#1E293B" BorderThickness="0" Padding="0" HorizontalContentAlignment="Center" VerticalContentAlignment="Center">
                  <TextBlock Text="0%" FontSize="11" FontWeight="Bold" Foreground="#8FA6BE" />
                </Button>
                <Button Name="SpkGain50"  Margin="2" Height="30" CornerRadius="8" Background="#1E293B" BorderThickness="0" Padding="0" HorizontalContentAlignment="Center" VerticalContentAlignment="Center">
                  <TextBlock Text="50%" FontSize="11" FontWeight="Bold" Foreground="#8FA6BE" />
                </Button>
                <Button Name="SpkGain100" Margin="2" Height="30" CornerRadius="8" Background="#1E293B" BorderThickness="0" Padding="0" HorizontalContentAlignment="Center" VerticalContentAlignment="Center">
                  <TextBlock Text="100%" FontSize="11" FontWeight="Bold" Foreground="#8FA6BE" />
                </Button>
                <Button Name="SpkGain150" Margin="2" Height="30" CornerRadius="8" Background="#1E293B" BorderThickness="0" Padding="0" HorizontalContentAlignment="Center" VerticalContentAlignment="Center">
                  <TextBlock Text="150%" FontSize="11" FontWeight="Bold" Foreground="#8FA6BE" />
                </Button>
                <Button Name="SpkGain200" Margin="2" Height="30" CornerRadius="8" Background="#1E293B" BorderThickness="0" Padding="0" HorizontalContentAlignment="Center" VerticalContentAlignment="Center">
                  <TextBlock Text="200%" FontSize="11" FontWeight="Bold" Foreground="#8FA6BE" />
                </Button>
              </UniformGrid>
            </StackPanel>
          </StackPanel>
```

- [ ] **Step 2: Add fields, presets, and events to the code-behind**

In `OrbitalSIP/Views/ActiveCallView.axaml.cs`, add these fields next to `_muted`/`_onHold` (around line 22):
```csharp
        private int _micGain = 100;
        private int _spkGain = 100;
        private static readonly int[] MicPresets = { 50, 100, 150, 200 };
        private static readonly int[] SpkPresets = { 0, 50, 100, 150, 200 };
```

Add these events next to the existing `OnMuteToggled`/`OnHoldToggled` event declarations (around line 363):
```csharp
        public event EventHandler<int>? OnMicGainChanged;      // arg = mic percent
        public event EventHandler<int>? OnSpeakerGainChanged;  // arg = speaker percent
```

- [ ] **Step 3: Wire and highlight the presets**

In `OrbitalSIP/Views/ActiveCallView.axaml.cs`, at the end of `WireButtons()` (after the last control is wired, before the method's closing brace around line 155), add:
```csharp
            WireGainPresets();
```

Then add these methods inside the class (e.g. after `WireButtons`):
```csharp
        private void WireGainPresets()
        {
            _micGain = App.SipService.MicGainPercent;
            _spkGain = App.SipService.SpeakerGainPercent;

            foreach (var p in MicPresets)
            {
                var b = this.FindControl<Button>($"MicGain{p}");
                if (b != null) b.Click += (_, __) => SetMicPreset(p);
            }
            foreach (var p in SpkPresets)
            {
                var b = this.FindControl<Button>($"SpkGain{p}");
                if (b != null) b.Click += (_, __) => SetSpkPreset(p);
            }
            HighlightPresets("MicGain", MicPresets, _micGain);
            HighlightPresets("SpkGain", SpkPresets, _spkGain);
        }

        private void SetMicPreset(int pct)
        {
            _micGain = pct;
            HighlightPresets("MicGain", MicPresets, _micGain);
            OnMicGainChanged?.Invoke(this, pct);
        }

        private void SetSpkPreset(int pct)
        {
            _spkGain = pct;
            HighlightPresets("SpkGain", SpkPresets, _spkGain);
            OnSpeakerGainChanged?.Invoke(this, pct);
        }

        private void HighlightPresets(string prefix, int[] presets, int selected)
        {
            foreach (var p in presets)
            {
                var b = this.FindControl<Button>($"{prefix}{p}");
                if (b == null) continue;
                bool sel = p == selected;
                b.Background = new SolidColorBrush(Color.Parse(sel ? "#2563EB" : "#1E293B"));
                if (b.Content is TextBlock tb)
                    tb.Foreground = new SolidColorBrush(Color.Parse(sel ? "#FFFFFF" : "#8FA6BE"));
            }
        }
```

- [ ] **Step 4: Verify it builds**

Run:
```
dotnet build OrbitalSIP/OrbitalSIP.csproj -c Debug
```
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add OrbitalSIP/Views/ActiveCallView.axaml OrbitalSIP/Views/ActiveCallView.axaml.cs
git commit -m "feat(ui): mic/speaker gain preset rows in ActiveCallView"
```

---

## Task 7: Connect the UI events in `MainWindow`

**Files:**
- Modify: `OrbitalSIP/MainWindow.axaml.cs`

- [ ] **Step 1: Subscribe to the preset events**

In `OrbitalSIP/MainWindow.axaml.cs`, inside `WireActiveCallView(callView)`, next to the existing `callView.OnMuteToggled += ...` / `callView.OnHoldToggled += ...` lines (around line 559), add:
```csharp
            callView.OnMicGainChanged     += (_, pct) => App.SipService.SetMicGain(pct);
            callView.OnSpeakerGainChanged += (_, pct) => App.SipService.SetSpeakerGain(pct);
```

- [ ] **Step 2: Verify it builds**

Run:
```
dotnet build OrbitalSIP/OrbitalSIP.csproj -c Debug
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add OrbitalSIP/MainWindow.axaml.cs
git commit -m "feat(ui): wire gain presets to SipService in MainWindow"
```

---

## Task 8: End-to-end manual verification

**Files:** none (verification only)

- [ ] **Step 1: Run the full test suite**

Run:
```
dotnet test OrbitalSIP.Tests/OrbitalSIP.Tests.csproj
```
Expected: PASS (6 `AudioGain` tests).

- [ ] **Step 2: Launch the app and place a real call**

Run:
```
dotnet run --project OrbitalSIP/OrbitalSIP.csproj -c Debug
```
Register, place or receive a call, open the expanded call view.

- [ ] **Step 3: Verify live behavior**

Confirm each of these by ear during an active call:
- Mic row shows 50/100/150/200; speaker row shows 0/50/100/150/200; the current level is highlighted (blue).
- Raising **speaker** to 150/200 makes the remote party louder; 0 % silences incoming audio.
- Raising **mic** to 150/200 makes you louder to the remote party; at 200 % on a loud voice the soft limiter prevents harsh crackle/wrap.
- The **Mute** button still mutes independently of the mic preset.

- [ ] **Step 4: Verify persistence**

Change mic to 150 % and speaker to 200 %, end the call, close and reopen the app, place another call. Confirm the presets are still highlighted at 150 % / 200 % and applied. Optionally confirm `MicGainPercent`/`SpeakerGainPercent` are written in `%APPDATA%\OrbitalSIP\sip-settings.json`.

- [ ] **Step 5: Final commit (if any verification tweaks were needed)**

```bash
git add -A
git commit -m "chore(audio): finalize gain boost feature after verification"
```

---

## Notes for the implementer

- **Why fork instead of a volume API:** verified against the shipped 8.0.7 DLL — `WindowsAudioEndPoint` has no gain/volume member, and OS-level WASAPI volume caps at 100 %. The forked class is the only way to reach 200 %. See the spec for the full rationale.
- **Thread safety:** `SourceGain`/`SinkGain` are plain `float` fields. Single-float reads/writes are atomic in .NET, so the UI thread can update them while the audio thread reads them without locking or tearing.
- **Unity is a true no-op:** at 100 % `AudioGain.Apply` returns immediately, so the default path is bit-for-bit unchanged from today's audio.
- **Keep the fork minimal:** the only authored changes to the copied file are the namespace/class rename and the two `AudioGain.Apply` insertions. Do not otherwise refactor it, so it stays easy to diff against upstream.
