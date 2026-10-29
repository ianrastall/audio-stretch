# AudioStretch

A Windows frontend for changing audio tempo with Rubber Band R3/Finer, preserving pitch. Fresh WinUI 3/C# interface and Rust processing core; follows Jupiter style standard 1.1 through the shared Jupiter.UI library.

## Install

Download **AudioStretch-0.2.1-Setup.exe** from [Releases](https://github.com/ianrastall/audio-stretch/releases/latest). Requires Windows 11 24H2 or newer, x64. The installer includes .NET and Windows App SDK; no development tools are needed. Search Start for **AudioStretch** after installing.

For a new installation, open **File > Settings > Updates and audio tools**, then select **Install Rubber Band** and **Install FFmpeg and FFprobe**. Packages download directly from their official upstream distributors. SHA-256 checks and executable version checks run before activation; upstream notices and previous tool payloads are preserved. Internet access is required for first setup. Audio processing stays local. Existing bundled tools from 0.2.0 remain usable.

The release is unsigned. Settings and separately installed audio tools live under `%LOCALAPPDATA%/Jupiter/AudioStretch` and survive app upgrades/uninstall. User audio is never removed by the installer. Close the app before running an installer manually.

## Use

1. Open one audio file or drop it on the window.
2. Set **Tempo (%)**: +5 means 1.05x speed and output duration = source duration / 1.05. Negative values slow it down. Exact typed values from -75% to +300% are accepted.
3. Choose WAV or FLAC, sample precision and an output location.
4. Select **Export...** or F5. Confirm replacement if the destination exists. **Cancel render** preserves the previous output.
5. Compare using **Play source** and **Play output**. Playback codec support depends on Windows; unavailable preview codecs do not prevent rendering.

WAV supports 32-bit float (default), 24-bit and 16-bit. FLAC supports 24-bit and 16-bit. Source sample rate and channel count are preserved for supported 8-192 kHz, 1-8 channel input. Working audio is floating-point; final integer export uses triangular dither at the target precision. Tags/artwork are not copied. No EQ or loudness normalization is added. Lossless export does not restore information absent from a lossy source.

All renders explicitly invoke `rubberband-r3 --fine`. Pitch remains unchanged in this release. Optional stereo centre focus is in Settings and is off by default. The main actions and progress stay visible below the scrolling form. Logs are bounded and copyable. Files are validated before final publication; source aliases, unapproved overwrites and destinations changed during rendering are rejected.

## Updates

**Help > Check for updates...** opens the settings window. Select **Check for updates** to check stable AudioStretch releases, the tested tool catalog and the official Rubber Band Windows download page. Daily checks on startup are optional and off by default; checking never installs anything or uploads audio.

- **AudioStretch:** download the newer release's installer, verify its SHA-256, then use an external helper to wait for the app to exit, install and reopen it. Settings and audio are preserved. Failures are recorded and shown on the next launch.
- **Rubber Band:** install/update independently from AudioStretch. A version is offered once its official archive/hash enters the tested catalog. Newer upstream versions outside that catalog are reported separately. New files are staged in their own version directory; activation changes only after hash/version validation. Failed/cancelled setup leaves the active version intact.
- **FFmpeg/FFprobe:** the same verified package installer supplies the tested audio conversion tools. No PATH or machine-wide installation is changed.

The catalog is [updates/tool-catalog.json](updates/tool-catalog.json); updating it can deliver a tested Rubber Band release without replacing the app. Current pinned packages are Rubber Band 4.0.0 and FFmpeg 8.1 essentials. Downloads retain the original archive and its notices in the user's data area. Tool installation is blocked while this app is rendering/probing.

## Full R3 frontend

This release provides the working tempo workflow and update infrastructure. The next expansion covers time/BPM/duration modes, independent pitch/formant control, dynamic time/pitch/frequency maps and meaningful R3 processing/diagnostic choices. See [the implementation plan](docs/full-r3-frontend.md), [all 33 CLI options mapped to frontend behavior](docs/r3-capability-matrix.md), and [the supplied XML manual](docs/rubberband-r3-help.xml). Planned controls are not advertised as implemented. R2-only switches will not be presented as R3 quality controls.

## Build and verify

Development root: `D:/jupiter/audio-stretch`. Runtime processing is in `core/`; `cli/` uses the same core; `proto/audio.proto` generates Rust/C# messages; `src/` contains the shell, SafeHandle bridge, update services and update host. Generated bindings and build artifacts are not edited by hand.

The source release ZIP includes sibling `audio-stretch/` and `jupiter-ui/` directories, including the exact shared UI source snapshot used for this release. GitHub's automatic repository-only source ZIP does not contain that separate dependency. Keep the two folders beside each other. Build prerequisites are pinned Rust 1.97.0, .NET SDK 10.0.113, Windows x64 build tools and Inno Setup 6. Audio tools are downloaded from upstream at runtime rather than bundled into the public installer.

From PowerShell 7 in the AudioStretch source directory:

```powershell
.\build.ps1
```

Normal builds produce an installer. `-AppOnly` is for development. `-Install` installs the built app for the current account. `-Verify` runs Rust formatting/lint/tests, managed/native tests, updater tests and real pitch/duration measurements. Verification needs a local legacy tool root (`-ToolsRoot`, default `D:/app`) and downloads the catalog archives for isolated setup tests. No global dependencies are installed by the build. Cargo/NuGet lockfiles are tracked.

The included CLI accepts a complete flat tools folder or the managed tools root:

```powershell
.\audio-stretch-cli.exe "$env:LOCALAPPDATA\Jupiter\AudioStretch\tools" D:\audio\input.wav D:\audio\output.wav 5 float
```

Arguments are tools root, input, absolute output, tempo change (%) and encoding (`float`, `24` or `16`). Output extension selects WAV/FLAC. Optional flags: `--overwrite`, `--centre-focus`.

See [verification](docs/verification-0.2.1.md), [decisions](docs/jupiter-exceptions.md), [historical audit](docs/legacy-audit.md) and [handoff](HANDOFF.md).

## Notices

See [third-party notices](THIRD-PARTY-NOTICES.md). The public installer distributes the AudioStretch application and its standard self-contained managed runtimes. Rubber Band and FFmpeg archives are fetched from their upstream distributors by the user-selected setup action, with upstream notices preserved. No additional application source license has been selected; public source availability is not itself a new license grant.
