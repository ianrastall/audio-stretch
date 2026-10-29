# AudioStretch Jupiter rebuild

Recorded 29 October 2026. Status: discovery and repository setup; implementation has not begun.

## Confirmed direction

- Write the application code fresh. The WPF application is a behavior reference, not a code base to port.
- Continue in `ianrastall/audio-stretch`, preserving its public URL, Git history, issues, stars and other repository identity.
- Work in `D:/jupiter/audio-stretch`.
- The primary workflow changes tempo while keeping pitch unchanged.
- Always use Rubber Band R3 / Finer, invoked as `rubberband-r3` with explicit `--fine`. Do not offer a faster R2 fallback.
- Keep this a standalone audio utility. YouTube production and the larger Audio Lab/MediaLab workflow do not define its scope.

## Repository continuity

The Jupiter checkout is a full clone of the existing GitHub repository, not a newly initialized repository. `origin` remains `https://github.com/ianrastall/audio-stretch.git`. Its default branch is `master`.

The `jupiter` development branch starts at `a973c43` (the existing WPF head). `legacy-wpf` preserves that same head. Leave the public default branch usable while building the replacement. Do not rewrite history, delete the repository, change its URL or replace it with an unrelated Git history. Merge the verified replacement into the existing default branch when ready. Preserve existing user assets and media; obsolete implementation files can be retired in an explained replacement commit once the new version works.

## Proposed first useful version

1. Open or drop an audio file; probe its actual audio stream independently of playback support.
2. Enter an exact tempo change, with a slider for coarse adjustment. Show both relative change and speed multiplier: +5% = 1.05x = original duration / 1.05.
3. Show source properties and expected output duration. Pitch stays unchanged.
4. Choose an output path and render using offline R3/Finer processing.
5. Show the current processing stage, cancellation and an accessible copyable log.
6. Play the source and completed result for comparison; preview failure must not prevent rendering.

Retain WAV output as the legacy baseline. FLAC output is a useful candidate, but the export formats, final bit depth and metadata policy still need to be settled. Do not inherit SongLab's fixed 24-bit/96 kHz mastering policy. Preserve source sample rate and channels where supported; reject or explain unsupported inputs rather than silently downsampling.

No EQ, mastering, loudness normalization, video generation or YouTube publishing is included in this first workflow. The legacy pitch/formant controls are outside the confirmed first workflow; their omission must be documented when the replacement is published. Batch processing and automatic BPM detection are not yet requirements.

## Architecture and interface

Follow `D:/jupiter/AGENTS.md`, the canonical stack reference and the Jupiter style standard: C# WinUI 3 shell, Rust domain core, generated protobuf messages through a small C ABI, and the shared Jupiter.UI library. Use its resources and appearance control and run the shared style check on every build. Do not use NumberBox.

Rust owns request validation, probe interpretation, tempo calculations, the processing plan, tool execution, cancellation and safe output publication. C# owns Windows file pickers, presentation, playback, window state and dispatching. A Rust CLI can exercise the same processing operations for verification and batch use without duplicating rules.

FFmpeg/FFprobe provide audio decoding, probing and final encoding; Rubber Band R3 performs the time stretch. Declare and pin dependencies in the relevant Cargo and project manifests. Prefer the already proven Jupiter build and packaging conventions for infrastructure while writing AudioStretch's application code fresh. Do not copy the old view model or processing implementation into the new architecture.

Use floating-point intermediate audio to avoid an unnecessary integer quantization before stretching. Final integer quantization and any dither belong at final export, with an explicit policy. Tool commands use argument arrays, a fixed audio-stream selection and invariant numeric formatting. Each render uses a captured immutable request.

Use unique working files. Validate output before committing it beside the destination; failed or cancelled work preserves both the source and any previous destination. Reject source/destination aliases, including hard links. Require explicit overwrite intent, and handle a destination that appears or changes during rendering. Cancellation and application shutdown must terminate and reap child processes before cleanup. Keep log storage bounded and avoid synchronous per-line UI dispatch.

## Quality setting evidence

`D:/app/rubberband/rubberband-r3.exe --help` confirms that `--fine` selects R3 and is already the default for that executable name. There is no numerical highest setting for `--fine`. Upstream documents that crispness controls tune R2; they are not additional R3 quality levels. Do not interpret “highest fine” as a fabricated slider or add ineffective crispness controls.

References: [official command-line overview](https://github.com/breakfastquay/rubberband#2-using-the-rubber-band-command-line-tool), [official API documentation](https://www.breakfastquay.com/rubberband/code-doc/classRubberBand_1_1RubberBandStretcher.html). Stereo centre focus is a separate audio tradeoff, not another fine-quality level; decide its default explicitly before implementation.

## Delivery and acceptance

A development executable alone is not a completed Jupiter GUI. The normal release build must produce an installable self-contained Windows x64 application with required tools, a Start Menu entry named AudioStretch, an uninstaller and a defined upgrade path. Preserve settings and user audio through upgrades and uninstall. Verify the installed app, Start launch and appearance setting on this machine.

Before release, prove a complete source-to-output render and cover:

- Speed/duration arithmetic and rejection of invalid, zero, negative and non-finite tempo values.
- A measured tone rendered faster and slower: duration changes as requested, while frequency stays unchanged within documented tolerances.
- Lossless and lossy sources, mono/stereo, high-resolution audio, Unicode/spaced paths, and a non-English numeric locale.
- Probe errors, unavailable preview codecs, missing tools and unsupported stream properties.
- Cancellation during decode/stretch/encode, tool failure, destination conflicts and input aliases; previous files survive and child processes exit.
- Native ABI/managed integration, UI resizing/keyboard/accessibility, shared style checks, installer launch and upgrade/uninstall preservation.

Inspect tool redistribution terms and include required notices/source-access information in the package. The existing repository has no license selected; do not invent a project license on the owner's behalf.
