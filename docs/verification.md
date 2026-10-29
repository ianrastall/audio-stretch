# AudioStretch 0.2.0 verification

29 October 2026. This is a local evaluation build. No public release has been created.

## Automated processing checks

- Rust formatting and Clippy with warnings denied pass.
- Seven Rust tests pass: tempo/duration math, invalid tempos, unknown precision and lossless codec handling, unsupported audio, source hard links/output conflicts, changed destinations and no-clobber/approved publication.
- Managed/native protobuf and ABI integration checks pass against the bundled real tools.
- Cancellation passes during decoding, R3 stretching and encoding. Each case preserves the previous output and removes working audio.
- Approved overwrite succeeds. Source overwrite and unapproved overwrite fail. A destination edited during processing is detected and preserved.
- German numeric culture still renders the exact 5.125% change requested through the managed client.
- Missing-tool errors, orderly shutdown cancellation and absence of orphan Rubber Band processes pass.
- WinUI Release build and shared Jupiter style check pass with zero warnings/errors.

Measured audio results (6-second 440 Hz source; frequency estimated from interpolated zero crossings after excluding the first/last half second):

| Tempo change | Output | Expected duration | Actual duration | Measured pitch |
| --- | --- | --- | --- | --- |
| +25% | WAV 32-bit float | 4.800000 s | 4.800000 s | 440.000009 Hz |
| -20% | FLAC 24-bit | 7.500000 s | 7.500000 s | 439.999997 Hz |
| +5.125% | WAV 16-bit | 5.707491 s | 5.707483 s | 439.999996 Hz |

Additional real-tool checks pass for 96 kHz stereo 24-bit FLAC with centre focus, MP3 input to lossless output, unchanged source hashes, and Unicode/spaced input and output filenames. Raw measurements are generated in `artifacts/audio-verification.json`; bundled-tool hashes are generated in `artifacts/tool-manifest.json`.

Commands: `cargo fmt --all -- --check`, `cargo clippy --locked --all-targets --target-dir artifacts/cargo -- -D warnings`, `cargo test --locked --target-dir artifacts/cargo`, and `pwsh -NoProfile -File build/build.ps1 -Verify -Install`. The build invokes `dotnet run --project tests/AudioStretch.Smoke/AudioStretch.Smoke.csproj -c Release -- artifacts/publish` and `python tests/verify_audio.py artifacts/publish`.

## Installed acceptance

The per-user installer and registered uninstaller complete with exit code 0. Start Menu registration is `AudioStretch` / app ID `Jupiter.AudioStretch`; launching its shortcut opens the installed application. Installed executable, managed assembly, native core and Rubber Band hashes match the tested publish.

An audio file was selected through the Windows picker and rendered through the installed GUI at +5%. FFprobe measured the six-second 48 kHz stereo 24-bit source's output as 5.714292 seconds, 48 kHz, stereo, 32-bit float. The completed output played through to its end in the built-in player (seek 100%, elapsed 5 seconds, remaining 0).

The appearance window opens through Ctrl+, and exposes the shared theme/accent controls. Light theme was applied and persisted, then Dark was restored. Default placement, the scrolling form and visible anchored actions were reviewed at 800x600. No Windows contrast-theme or comprehensive assistive-technology evaluation was performed.

An installed update preserved the settings file byte-for-byte. Uninstall removed the Start shortcut while preserving the settings and both source/output audio hashes; reinstall restored the shortcut and retained the same settings. Raw lifecycle results are in `artifacts/lifecycle-verification.json`.

The lifecycle exercise exposed an exception when closing the main window after Settings had already closed. The final installed build checks the Settings window's closed state and queues the final main-window close after asynchronous cleanup. That correction compiled and installed successfully; installed file hashes match the final publish. The exact GUI shutdown sequence was not repeated after this correction because the app was minimized and desktop input interrupted further automation. The historical error log was retained; no later entries were observed. This remains a specific manual regression check for the evaluation build, rather than a claimed passed test.

## Limits

Synthetic tone checks establish arithmetic and pitch preservation for the measured cases, not perceptual quality on every kind of music. User listening evaluation is the next step. Playback depends on Windows codecs. Tags/artwork are omitted by design. The application remains unsigned and public binary distribution remains deferred; see third-party notices and the licensing/release section in the readme.
