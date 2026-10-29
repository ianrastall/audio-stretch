# AudioStretch

Change audio tempo while keeping pitch unchanged, using Rubber Band R3/Finer. A standalone Windows audio utility with a fresh WinUI 3 interface and Rust processing core. Follows Jupiter style standard 1.1 and imports the shared Jupiter.UI library.

Version 0.2.0 is a local evaluation build. The WPF implementation has been removed from the active source tree; its history remains in this same repository. The GitHub repository and public URL are unchanged. No new public binary release has been published.

## Use

Install AudioStretch-0.2.0-Setup.exe, then search Windows Start for **AudioStretch**. Requires Windows 11 24H2 or newer, x64. The installer includes .NET, Windows App SDK, FFmpeg, FFprobe, Rubber Band R3 and its companion DLL. No PATH setup or separate tool download is required. Processing runs locally without network access.

1. Open an audio file or drop one onto the window.
2. Set **Tempo (%)**: +5 means 1.05x speed, with an output duration of original duration / 1.05. Negative values slow it down. Exact typed values are supported from -75% to +300%; the slider offers coarse adjustment. Pitch stays unchanged.
3. Choose WAV or FLAC, sample precision and an output location.
4. Select **Export...** or press F5. Existing destinations require confirmation. Use **Cancel render** to stop.
5. Use **Play source** and **Play output** to compare, with the player's transport controls for seeking and pause. Playback codec availability depends on Windows; a playback failure does not prevent processing.

WAV supports 32-bit float (default), 24-bit or 16-bit. FLAC supports 24-bit or 16-bit. Source sample rate and channel count are preserved; supported input is 8-192 kHz, 1-8 channels. Working audio is floating-point. Integer export applies triangular dither at final quantization. No EQ, loudness normalization or automatic resampling is added. Source tags and artwork are not copied. Exporting a lossy source to WAV/FLAC avoids another lossy encode but cannot restore missing information.

All renders use the separate rubberband-r3 tool with explicit --fine. No R2 quality mode or pitch-shift control is exposed. **File > Settings** (Ctrl+,) provides the shared theme/accent controls and an optional stereo centre-focus setting; that setting applies to future renders and is off by default. It is a separate stereo imaging choice, not an extra R3 quality level.

Rendering uses unique temporary files in the output folder and validates duration, sample rate and channels before publication. Source aliases and unapproved overwrites are rejected. Cancellation or failure preserves the previous output and cleans up working audio. External tools run in Windows jobs that terminate on application exit. Processing stages are shown without a fabricated percentage or ETA. The processing log is bounded and can be copied.

Settings live at %LOCALAPPDATA%/Jupiter/AudioStretch/settings.json. Upgrades and uninstall preserve that folder and user audio. A stable per-user installer ID supports future upgrades. The application is unsigned; this build is for local evaluation before public release.

## Develop

The working checkout is D:/jupiter/audio-stretch, continuing ianrastall/audio-stretch with full Git history. The GUI expects the sibling D:/jupiter/jupiter-ui shared library. Native processing, the CLI and core tests do not require the UI library.

- core/: Rust validation, probing, audio pipeline, safe output handling, cancellation and ABI 1.
- cli/: command-line frontend over the same core.
- proto/audio.proto: source of generated Rust and C# request/reply messages.
- src/AudioStretch.Interop/: managed SafeHandle boundary and typed client.
- src/AudioStretch.App/: WinUI 3 shell, Windows file pickers/playback, settings and window placement.
- build/: native build, self-contained publish and Inno Setup installer.
- tests/: managed/native failure/cancellation checks and measured pitch/duration verification.

The repository pins Rust 1.97.0 and .NET SDK 10.0.113 to the installed, working Jupiter toolchain; Cargo and NuGet lockfiles capture dependencies. The Windows App SDK stays on the shared Jupiter.UI-compatible stable 2.5.1 baseline.

From PowerShell 7 in this repository:

```powershell
.\build.ps1 -Verify -Install
```

The normal build produces artifacts/installer/AudioStretch-0.2.0-Setup.exe. -AppOnly produces the development publish without the installer. -ToolsRoot selects an existing tool root containing ffmpeg/ffmpeg.exe, ffmpeg/ffprobe.exe, rubberband/rubberband-r3.exe and rubberband/sndfile.dll; default D:/app. Tool hashes are recorded in artifacts/tool-manifest.json. Inno Setup 6 is required for packaging. No global tools are installed by the build.

The native CLI also works without the GUI:

```powershell
.\artifacts\publish\audio-stretch-cli.exe .\artifacts\publish\tools D:\audio\input.wav D:\audio\output.wav 5 float
```

Arguments are tools folder, input, output, tempo change (%) and encoding (float, 24 or 16). Optional flags: --overwrite, --centre-focus. Output extension selects WAV or FLAC. Input/output paths supplied to the core must be absolute; use full paths for actual processing.

See [verification](docs/verification.md), [decisions and exceptions](docs/jupiter-exceptions.md), [project brief](docs/jupiter-project-brief.md), [historical source audit](docs/legacy-audit.md) and [handoff](HANDOFF.md).

## Licensing and release

No application license has been selected. See [third-party notices](THIRD-PARTY-NOTICES.md). Public binary release preparation must resolve application licensing and supply the matching tool distribution notices and corresponding-source materials for the exact bundled binaries. Local installation and user evaluation come first.
