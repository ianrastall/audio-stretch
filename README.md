# AudioStretch

A lightweight, high-quality GUI focused on precise tempo adjustments, powered by `rubberband-r3` for best-in-class time-stretching and `FFmpeg` / `ffprobe` for broad audio format support.

## Disclaimer: AI-Generated Codebase
**Please note:** This entire codebase was generated using AI assistants (Claude, Gemini, and ChatGPT). While the application is functional and very unlikely to cause any harm to your system, the code itself might be somewhat brittle or unconventional in places. It is entirely possible that some architectural wiring isn't perfectly in place or that edge-case bugs exist. Use at your own risk, and feel free to contribute if you spot something that needs refactoring!

## Features
- **High-Quality Tempo Stretching:** Uses the industry-standard `rubberband` library to adjust audio tempo without altering pitch.
- **Broad Format Support:** Import FLAC, MP3, M4A, OGG, WAV, and more. FFmpeg automatically handles decoding underneath the hood.
- **Smart Probing:** Uses `ffprobe` to read the exact sample rate, bit depth, and channel count of your source audio, ensuring maximum fidelity during the stretch.
- **Lossy Input Warnings:** Automatically detects lossy formats and warns when quality might be degraded by re-encoding.
- **Portable & Self-Contained:** Built as a single-file executable for Windows 10/11 x64.

## Prerequisites & External Tools
AudioStretch relies on three external command-line tools to function. These tools are kept external to keep the executable lightweight and allow for easy updates.

The **External tools** bar at the top of the window shows whether each tool was found. If any are missing, click **Download & install** and AudioStretch will fetch them from their official sources and unpack them into the `tools/` folder for you:
- `ffmpeg.exe` and `ffprobe.exe` — from the [gyan.dev](https://www.gyan.dev/ffmpeg/builds/) release-essentials build linked by ffmpeg.org (~103 MB download; the two static binaries are ~100 MB each once unpacked).
- `rubberband-r3.exe` (and its required `sndfile.dll`) — from the [Rubber Band](https://breakfastquay.com/rubberband/) command-line release.

The app checks your system `PATH` first, so anything you already have installed won't be re-downloaded. To set the tools up manually instead, place those files in a `tools/` folder next to `AudioStretch.exe` (or anywhere on your `PATH`).

## Building from Source
The application is a standard WPF C# project using .NET.

To build a single-file, self-contained executable for Windows x64:
1. Open PowerShell in the project root.
2. Run the included build script:
   ```powershell
   .\build.ps1
   ```
3. The script will publish the project and automatically copy the resulting `AudioStretch.exe` to the root folder.

## License
*(Add your license information here)*
