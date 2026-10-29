# AudioStretch handoff

## Current 0.3.0 implementation

29 October 2026. The complete R3 plan is implemented, tested and installed locally as 0.3.0. Release target: v0.3.0 in the same repository. The historical 0.2.1 notes below are superseded where they describe planned controls.

GUI/CLI share explicit tempo percentage/speed, BPM, duration multiplier/target duration, semitone/cents/frequency, formants, centre focus, real-time file rendering, draft windows, real-time pitch-HQ, clipping and quiet/debug controls. Defaults remain offline R3/Finer with unchanged pitch. Debug 3 audible ticks require opt-in. No R2-only/no-op controls. MainWindow.Processing.cs forwards rules/conversion to core/src/transform.rs through protobuf; MapsWindow edits source-bound rows/native text in frames/seconds; HelpWindow renders supplied XML and actual installed help/full-help/version.

Maps bind source hash, decoded sample count and rate. Source replacement deliberately clears active maps. Time maps require aggregate duration/offline. Pitch/frequency maps force real-time/high-consistency and override pitch-HQ; combining them with time maps is rejected. Native time maps omit the implicit `0 0` origin to avoid a measured R3 4.0.0 zero-division bug and validate/add final duration anchors. Keep that regression and supplied XML intact. CLI frame cap is 2,147,483,647. Lossy probes decode/count actual gapless samples.

Command preview, JSON jobs, PowerShell scripts and successful receipts capture settings/maps, source/output hashes/properties, actual duration/error, exact arguments, tool paths/versions/hashes and clipping logs. Jobs disable overwrite and reject changed sources. Replay uses current tools; compare hashes. Existing safe publication/cancel/alias/destination checks remain; source mutation rejects publication and no success receipt is emitted on failure.

`build.ps1 -Verify` passes 11 Rust tests/Clippy/formatting, native/managed/updater tests, legacy audio checks and 22 full-R3 audio acceptance cases. Final UI was rebuilt/installed. Start Menu launch, map editing/preview/Apply, actual GUI export, receipt saving and F1/installed help were verified. Original preferences were restored byte-for-byte after UI testing; the installed app was reopened. See docs/verification-0.3.0.md and ignored artifacts/ evidence. Synthetic voiced/tone/impulse checks do not replace perceptual listening on real music.

Build/package-source versions are 0.3.0. After committing, package the exact sibling Jupiter.UI snapshot and publish installer/source ZIP/checksums. Preserve unrelated shared workspace changes. Application/tool update infrastructure and official catalog from 0.2.1 remain; no new audio tool version is assumed tested. Public installers contain app/runtimes, and explicit setup downloads official tool archives. Settings/data/notices/audio remain preserved. No new application source license is inferred.

## Historical 0.2.1 snapshot

29 October 2026. Version 0.2.1 is published and installed locally.

## Current direction

The owner confirmed the initial build works and explicitly asked for a public release now in the original ianrastall/audio-stretch repository, followed by a design for the full Rubber Band R3 frontend and update support for both app and Rubber Band. This supersedes the earlier release hold and tempo-only future scope. Keep tempo/pitch-preserved as the default workflow; R3/Finer remains the selected engine.

## Implemented

Fresh WinUI 3/Jupiter.UI shell and Rust audio core, generated protobuf/SafeHandle ABI 1, native CLI, float decode/R3 stretch/final WAV or FLAC export, validation and safe publication, cancellation, source/result playback, settings/placement and installer are retained from 0.2.0. WPF is removed from the active source tree. Sample audio and original art are preserved.

0.2.1 adds a dependency-free .NET update service and self-contained update host. Settings offers manual checks and opt-in daily checks, app update installation, and separate Rubber Band/FFmpeg installation. The app checks GitHub stable releases with installer hashes. The helper is copied outside the app folder, waits for the app PID to exit, rechecks the installer hash, installs and relaunches. Result JSON/logs are under user data. Tools download directly from official upstream ZIPs; catalog hashes and actual executable versions are checked before an atomic active-pointer switch. Failed/cancelled setup preserves the active version, and prior payloads/notices remain available. Rust resolves the managed per-tool version folders first and legacy bundled tools as fallback.

Public installers include the application and runtimes but no audio tool binaries. Users select initial tool installation in Settings. This avoids publishing the old local-only tool bundle, whose exact distribution/source notices had not been packaged. The first public release target is 0.2.1. No new application source license is inferred or granted.

## R3 frontend plan

The owner added docs/rubberband-r3-help.xml. It matches the SHA-256 of the R3 binary in the official 4.0.0 Windows ZIP. docs/r3-capability-matrix.md maps all 33 options; docs/full-r3-frontend.md specifies constant time/BPM/duration/pitch/formant controls, then typed dynamic maps and mode-aware validation, then remaining meaningful R3 processing/diagnostics. R2-only and ignored flags are reference material rather than fake R3 quality controls. Extra controls are not yet implemented.

## Build, source and tests

Run .\build.ps1 -Verify for the installer plus Rust, managed/native, updater and actual audio tests. -Install installs locally; -AppOnly is development. The build pins Rust/.NET/NuGet/Cargo versions and imports the canonical sibling Jupiter.UI library/style check. Audio tools are no longer copied to the public publish folder. Verification-only tools/caches stay under artifacts/ and work/.

Run build/package-source.ps1 after committing release source to produce a source ZIP with audio-stretch/ and the exact tracked working snapshot of sibling jupiter-ui/. It records shared file hashes and does not change/commit unrelated Jupiter.UI modifications. GitHub's automatic repository-only source archive lacks that separate dependency; publish the complete source ZIP too.

All automated checks described in docs/verification-0.2.1.md pass, including official package setup and native processing through managed tool folders. The public latest stable release is https://github.com/ianrastall/audio-stretch/releases/tag/v0.2.1 at source commit 5cb1b9465787863affb4474b2126d7980fa0887c, with installer, complete source ZIP and SHA256SUMS.txt. Remote asset digests match local files; source ZIP layout/locked restore pass.

The actual live update client discovers 0.2.1 as an upgrade from 0.2.0, validates its installer cache against the published digest, reads the official Rubber Band Windows release, and installs the official catalog packages into the account's managed tool folder. The external self-contained helper was tested outside the install directory with a controlled parent process: it waited, installed, preserved settings and relaunched the app. Installed version is 0.2.1.0. See artifacts/helper-verification.json and artifacts/live-update-verification.json for local raw evidence. No automated mouse/keyboard activity was needed for these release checks.

## Data and limits

Data/settings/tools/cache: %LOCALAPPDATA%/Jupiter/AudioStretch. Updates and uninstall preserve settings, audio and separately installed tools. Original repo URL/history remain intact. No telemetry or audio uploads. Checks contact GitHub and Breakfast Quay only when requested or opt-in daily checks run.

Unsigned binaries; Windows codec-dependent playback; source tags/artwork omitted. Synthetic tones do not establish perceptual quality on all music. Full R3 controls remain planned. Catalog updates require hash/version/help/processing validation before offering a new upstream version. Keep checks separate from installation. See docs/jupiter-exceptions.md for architecture/behavior details and prior filesystem/crash cleanup limits.
