# Legacy implementation review

Inspected 29 October 2026 at commit `a973c43`. Static inspection only: no build or runtime validation of the old GUI was performed. These findings describe code paths, not measured audio outcomes.

## What exists

The repository is `https://github.com/ianrastall/audio-stretch`, created 18 June 2026. The source is a C#/.NET 10 WPF x64 application using CommunityToolkit.Mvvm and NAudio. `App.xaml` starts `MainWindow.xaml`, whose data context is `MainViewModel`. `build.ps1` publishes a self-contained single-file executable. It does not produce an installer.

The application opens one audio source, previews it through NAudio/Media Foundation, decodes with FFmpeg, stretches with Rubber Band and saves WAV output. It has tempo, pitch, formant and stereo centre-focus controls, process cancellation, progress/log views and an in-app external-tool downloader. The readme does not describe all controls added by the latest commit.

The existing source already explicitly requests `rubberband-r3 --fine`; a fresh implementation is not needed to unlock R3 itself.

## Behavior worth preserving

- Source and output paths cannot be the same normalized path.
- Existing output requires user confirmation.
- Rubber Band writes to a unique temporary file beside the destination before success publication.
- Cancellation kills the active process tree, then waits before cleanup.
- Output path customization is tracked separately from automatic naming.
- Numeric Rubber Band arguments use invariant culture.

## Concrete weaknesses

| Location | Finding | Consequence for the new design |
| --- | --- | --- |
| `AudioConverter.cs`, `ProbeAsync` | Missing/unknown bit depth becomes 16-bit. | Unknown precision is not evidence that the source should be decoded to 16-bit. |
| `AudioConverter.cs`, `ConvertToWavAsync` | Decode uses integer PCM for sources reported as 16- or 24-bit and clamps sample rate to 192 kHz. | Use a deliberate floating-point intermediate policy; make unsupported-rate handling explicit. |
| `MainViewModel.cs`, `PickInput` / `RunAsync` | Render progress obtains duration from the preview player. A failed preview does not establish a new independent duration. | Probe duration independently and clear per-source state. Rendering must not rely on playback support. |
| `MainViewModel.cs`, Rubber Band event handlers | Percentage parsing is attached to stdout; stderr is logged without progress parsing. | Verify the real tool output stream and carriage-return behavior before promising accurate percentages or ETA. |
| `MainViewModel.cs`, `RunAsync` | Probe/decode failure returns can leave the displayed stage as “Probing” or “Decoding.” | Every execution path needs a final success, cancellation or error state. |
| `MainViewModel.cs`, `Append` | Every log line synchronously invokes the UI dispatcher and appends to an unbounded collection. | Buffer/batch logs and cap retained UI content. |
| `MainViewModel.cs`, `BuildRubberbandArgs` | Tempo is formatted to three decimal places and `--ignore-clipping` is always added. | Preserve requested precision and define clipping/headroom handling explicitly. |
| `MainViewModel.cs`, final output commit | Overwrite confirmation happens before processing, followed by an unconditional overwrite move. | Recheck destination identity/change at commit time and define conflict behavior. |
| `MainViewModel.cs`, `SamePath` | Equality compares normalized strings. | Also protect against hard-link and other filesystem aliases. |
| `AudioConverter.cs`, FFmpeg arguments | No explicit `-map` selects the probed audio stream. | Probe and decode must refer to the same stream. |
| `AudioConverter.cs`, lossy detection | Filename extension determines the warning; for example, M4A may hold lossless ALAC. | Use the probed codec and explain that lossless export does not restore source information. |
| `DependencyInstaller.cs` | FFmpeg comes from a moving archive URL; no expected archive hash is checked. Missing expected archive contents produce warnings. | Pin and verify release inputs; fail on missing tool files and validate actual execution. |
| Repository / `build.ps1` | No test project, no installer, no selected application license. | Build verification and complete Jupiter delivery into the replacement from the beginning. |

These findings justify separate processing and presentation layers. They do not require retaining any of the WPF application code in the replacement.

## Discovery limits

No standalone matching checkout was found in the initial `D:/GitHub` or `D:/dev/proj` directory inventories. A broader filename search of several older roots also did not locate AudioStretch. This is not a claim that no other local copy exists anywhere. The new working checkout was cloned directly from GitHub with full history into `D:/jupiter/audio-stretch`.
