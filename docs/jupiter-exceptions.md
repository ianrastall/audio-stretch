# Jupiter conformance and decisions

29 October 2026. AudioStretch 0.2.0 follows Jupiter style standard 1.1. Its project imports Jupiter.UI.targets with the style check in Error mode. It uses shared resources, native title bars and the focused utility layout. There are no local color/type/spacing resources and no NumberBox or backdrop.

## Architectural choices

- The small focused form uses native C# event handlers for Windows pickers, playback and presentation rather than a separate MVVM toolkit layer. All audio validation, processing, tool supervision and output publication remain in Rust behind generated protobuf and the typed managed SafeHandle client. This avoids an additional dependency solely to forward form events; it does not create a second processing authority.
- Processing uses supervised external command-line tools rather than linking Rubber Band or FFmpeg into the GUI. Tool failures are isolated from managed/native UI execution, and the selected `rubberband-r3 --fine` path is observable in the log.
- Rust 1.97.0 and Windows App SDK 2.5.1 are retained from the installed/proven Jupiter toolchain and shared UI baseline. Newer releases exist; a coordinated SDK/UI upgrade is separate work.

## Behavior decisions

- WAV float is the default. WAV integer and FLAC are explicit export choices. Source sample rate/channels are preserved; unsupported inputs fail rather than being silently converted. No source metadata/artwork is copied in this version.
- R3 has no extra numerical fine level. The app always requests Finer. Stereo centre focus is an independent optional setting, disabled by default.
- Determinate percentages and ETA are omitted because the pipeline has no reliably weighted total; real stages and elapsed time accompany indeterminate progress.
- The 800x600 initial focused utility window scrolls its form while keeping the primary action, cancellation and progress in view below it; it restores later placement and imposes the 640x480 minimum. Appearance settings use a separate 720x720 window and the shared controls.
- Existing destination identity, size and modification time are rechecked before replacement. Publication is an atomic same-volume Windows move. A destination absent at start is published without replacement permission. Replacement requires releasing compared handles immediately before the final move; an unrelated writer can still race that final check/move interval. The app is a single-user utility, not a filesystem transaction coordinator.
- Working audio lives in a unique folder next to the output. An orderly cancel/failure/shutdown removes it. An OS crash or forced process termination can leave that folder for manual removal, while the source and final destination remain protected until publication.

No visual exceptions are currently required.

## 0.2.1 update and distribution decisions

The owner authorized release publication and network update checks/setup on 29 October 2026. C# owns Windows package download/installation and app lifecycle updates; Rust remains the authority for audio processing. The update service uses only .NET base class libraries, with no additional third-party package dependency.

Public installers include the app and managed runtimes; audio tools are installed explicitly from upstream after setup. This is a deliberate packaging change from the local 0.2.0 bundle. The setup UI and installation notes disclose the initial Internet requirement; offline processing remains available after setup. Tool mutation is blocked while this app is processing. Automatic checks are off by default, and never install software.

Full R3 processing is implemented in 0.3.0. R2-only options remain excluded from R3 controls. Offline duration tolerance is max(0.1 seconds, 0.01%); real-time file rendering allows 0.5 seconds + 2%, and always reports actual duration/error.

## 0.3.0 maps and reproducibility

The main focused utility retains optional sections. A separate map editing window uses the same 720x720 secondary-window placement and shared resources. Editable row pairs, native text import/export, unit conversion and normalized previews remain presentation; Rust owns all parsing, validation, normalization and option rules. Each map is bound to the decoded source frame count, rate and SHA-256. Replacing the source requires clearing the active maps. Map-family drafts are kept within the editor session; Apply selects one meaningful family.

The Rubber Band 4.0.0 R3 implementation initializes the first map ratio by dividing target frame by source frame. Explicit `0 0` therefore causes NaN and resets the initial ratio. The editor accepts the implicit origin, while the normalized native map omits it and appends a validated final duration anchor. Time maps and real-time/pitch maps are rejected together, because upstream ignores time maps in real-time mode.

Jobs and successful receipts capture the typed request and immutable maps. Receipts also include actual source/output hashes, tool versions/hashes, effective commands, duration error and clipping messages. Saved jobs disable overwrite and reject changed source identities; replay uses currently selected tool versions, with their hashes exposed for comparison. Exact bit-for-bit output across tool versions is not promised. Optional scripts use PowerShell literal argument quoting and call the shipped CLI through the same Rust authority.

Compressed-source probes decode/count actual samples so map positions account for gapless delay/padding. This makes probing long lossy files more expensive. The supported CLI source/output/map limit is 2,147,483,647 sample frames, checked before rendering. No new visual exceptions are required.
