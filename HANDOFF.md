# AudioStretch handoff

29 October 2026. Version 0.2.0, local evaluation before public release.

## Direction

The owner wants fresh application code in the original ianrastall/audio-stretch repository. Its public URL and history are retained. WPF is removed from the active source tree. This is a standalone tempo utility: pitch unchanged, always rubberband-r3 --fine. Local installation and listening evaluation precede any public GitHub release.

## Implementation

- Rust core owns probe interpretation, tempo validation, float decode, R3 stretch, output encoding/validation, cancellation and destination safety. The CLI uses the same core.
- WinUI 3/C# shell uses shared Jupiter.UI, generated protobuf and the SafeHandle managed client. Native ABI version is 1; calls on one engine are serialized. A worker publishes bounded status/log snapshots; cancellation is polled while supervising real tools in Windows jobs.
- WAV float (default), WAV 24/16-bit, FLAC 24/16-bit. Sample rate/channels preserved for supported 8-192 kHz, 1-8 channel audio. Explicit final integer bit depth/dither; no source tags/artwork, EQ, loudness normalization or pitch control.
- Open/drop, exact tempo text plus coarse slider, source/result playback, overwrite confirmation, cancel, copyable log, settings, saved placement. Actions/progress remain visible below the scrolling form.
- Stable per-user Inno Setup installer, Start Menu AudioStretch, registered uninstaller; runtime/tools bundled. Build outputs are under artifacts/. No global installation of dependencies.
- Settings: %LOCALAPPDATA%/Jupiter/AudioStretch/settings.json. Never delete settings or user audio during upgrades/uninstall.

## Commands and verification

Run .\build.ps1 -Verify -Install in PowerShell 7. Normal builds produce the installer; -AppOnly is for development. Inno Setup 6, the pinned toolchains, sibling ../jupiter-ui and an existing tools root (default D:/app) are prerequisites. See README.md for individual build/test commands.

Seven Rust tests, formatting, Clippy, managed/native integration and measured real-audio tests pass. The shared style check and self-contained Release publish pass. Tests cover cancellation during all three tool stages, preserving old output, output conflicts/aliases, changes during processing, missing tools, shutdown cleanup, Unicode paths, German numeric culture, stereo96/24 FLAC, lossy inputs and unchanged measured pitch. See docs/verification.md for measurements and installed acceptance status.

A Windows file-handle replacement bug found by the integration tests was fixed by opening comparison handles with delete sharing and releasing them before the final same-volume move. A first-run layout issue found visually was corrected by anchoring the actions/progress below the scrolling form. Integer dither specifies the target bit depth explicitly. Closing Settings and then the main window exposed a repeated-close exception; the shell now checks Settings.IsClosed and queues its final main-window close after cleanup. These corrections are in the current source.

## Files and repository

New code lives in core/, cli/, proto/, src/, build/ and tests/. The old root WPF code/project/build script were retired; build.ps1 now forwards to the new build. Sample WAVs and original artwork remain preserved. Historical findings are in docs/legacy-audit.md; docs/jupiter-project-brief.md retains the original design context with a current-state note. docs/jupiter-exceptions.md records architecture and behavior decisions.

The Jupiter implementation descends from original WPF head a973c43 and continues the same repository history. The installed replacement is integrated into master and jupiter; legacy-wpf is only a historical branch. Do not rewrite history or create a replacement repository. No public release is authorized before local evaluation is complete.

Installed GUI export/playback, shared theme changes, settings preservation through update/uninstall/reinstall, Start registration and final installed-file hashes were verified. The final Settings-close guard was compiled and installed; its exact close sequence remains a manual regression check after desktop input/minimization interrupted the last automation pass. AudioStretch is available from Start for the owner. See docs/verification.md for the distinction between observed passes and that remaining check.

## Remaining limits

Playback codec support depends on Windows. Tone checks do not prove perceptual quality for every song. Atomic approved replacement has a small external-writer race between destination recheck and final move. Forced OS/process termination can leave working folders for manual cleanup. The app is unsigned. Application licensing is unselected; matching third-party notices and corresponding source materials must be prepared before public binary distribution. Public source-build instructions also need a distributable Jupiter.UI source/package dependency; this machine currently uses the sibling shared workspace. User listening evaluation remains the next product step.
