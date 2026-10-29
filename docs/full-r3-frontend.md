# Full Rubber Band R3 frontend

29 October 2026. Implemented in AudioStretch 0.3.0. This document records the accepted scope and acceptance requirements; see verification-0.3.0.md for measured results and limitations.

## Scope and evidence

The supplied `rubberband-r3-help.xml` contains 33 long options and records exact parser/processing semantics for 4.0.0. The official downloaded Windows archive contains an R3 executable whose SHA-256 matches that manual. See [the complete capability matrix](r3-capability-matrix.md). Keep the XML intact as source material and generate human-readable reference views from its stable option IDs.

R3/Finer remains the engine and the default quality. A complete R3 frontend covers R3 capabilities without implying that R2-only knobs improve R3. Preserve the simple tempo workflow as the initial surface; advanced processing expands it rather than replacing it with a wall of switches.

## First expansion: constant transformations

Add a **Time** mode selector with four equivalent ways to specify one aggregate duration relationship:

- Tempo change (%) / multiplier: existing workflow; a positive tempo change shortens duration.
- Original and target BPM: `--tempo X:Y`, with finite positive values.
- Duration multiplier: `--time`, where a value above 1 lengthens audio.
- Target duration: `--duration` in hours/minutes/seconds, resolved from probed audio duration.

Only one time mode is active per request, so users do not accidentally depend on the CLI's multiplication/override order. Rust computes and exposes expected duration and the effective ratio. Keep text values exact until validated; use checked, locale-independent conversion to CLI arguments.

Add independent **Pitch** with semitone/cents or frequency-ratio entry and optional **Preserve formants**. Store the normalized effective frequency ratio and the chosen entry mode, allowing equivalent transforms without forcing users to understand multiplication of overlapping flags. Pitch-only operation must supply a neutral time ratio or pitch value accepted by the CLI. At initial settings pitch remains unchanged.

Add R3 stereo centre focus with its actual tradeoff explained. Retain offline analysis as the normal render mode and the existing sample-rate/channel preservation, float intermediates, output validation, cancel and overwrite behavior. Do not bring back EQ/mastering/YouTube scope.

Acceptance: measured pitch-only, time-only and combined transforms; ±12 semitones, frequency ratios, BPM conversions and exact duration; formant effects assessed on voice/music rather than inferred from a sine wave; validation independent of every shell.

## Second expansion: dynamic maps

Provide a dedicated map editor with rows and import/export of the native text format. Time maps show source time/frame and destination time/frame. Pitch maps show source time/frame and semitone offset; frequency maps use a positive multiplier. Convert between displayed seconds and integer sample frames using the probed sample rate, with checked bounds and explicit rounding.

- Time maps also require an aggregate time/tempo/duration setting. Show the resulting total duration separately from intermediate anchors.
- Pitch and frequency maps are mutually exclusive. Offsets are relative to the initial pitch setting, not cumulative.
- Preserve the map's source sample-rate identity. Replacing/resampling a source must not silently reinterpret anchors.
- Validate monotonically ordered frame keys and meaningful time-map targets. Explain duplicate keys and resolve them deliberately; do not silently rely on the CLI's last-entry-wins behavior.
- Snapshot the validated map contents into the render's working folder. External edits after Run must not alter that job.
- Pitch/frequency maps force the library's real-time/high-consistency mode. “Real-time” here remains file rendering, not microphone/live playback. Exact-duration validation must become mode-aware; the current strict offline check cannot be applied unchanged.

Acceptance: impulses at known frames verify time anchors; tones/chirps verify pitch-map offsets; malformed maps, duplicates, mutually exclusive maps, changing source rates, long files and cancellation preserve data. Show actual resulting duration/error after every map render.

## Third expansion: processing and diagnostics

Expose only options with an effect in the selected R3 mode. The normal path stays Finer/offline. R3 short-window processing is an explicitly labeled draft alternative, never another purported highest-quality setting. Validate real-time pitch-quality interactions against the exact supported binary. Map-driven high-consistency mode takes precedence over ordinary real-time pitch quality.

Clipping choices must describe real behavior: default restart at reduced gain (with the upstream minimum-gain/clamp limit), or clamp without restart via `--ignore-clipping`. Record any retry/gain behavior in the result. Neither choice is loudness normalization.

Provide command preview/export and a reproducible render receipt: effective arguments, tool version and hash, maps, source properties, output properties and warnings. Quiet/debug settings belong in diagnostics. Debug level 3 adds audible ticks and needs an explicit diagnostic-output workflow, rather than being an ordinary export default.

`--fast`, crispness/transient/detector/lamination controls and R2 thread tuning remain documented reference material, outside the R3 processing controls. `--loose` and `--precise` are ignored compatibility options and get no fake enabled controls. Version/help switches become tool information and help. Optional queue/preset work follows these processing capabilities; it is not a prerequisite for the initial release.

## Architecture and rollout

Extend `proto/audio.proto` with explicit time mode, pitch settings, processing mode and typed map messages; reserve existing field numbers. Rust owns normalization, validation, option compatibility, map serialization, command construction, immutable job capture and mode-aware output verification. Both CLI and WinUI consume the same operation model. The shell owns editing, selection, pickers and playback.

Move to a Jupiter workbench only when the map editor needs persistent space. Until then a focused utility with clearly named optional sections is sufficient. Use shared Jupiter.UI resources and no NumberBox. Implement and test constant transformations first, then maps, then remaining meaningful R3 diagnostics. Each delivered control must execute real behavior before it is enabled.

## Updates

0.2.1 separates application and tool lifecycles. Application checks use GitHub stable releases and verified installers. Tool checks read the versioned hash catalog plus the official Rubber Band Windows download page. The catalog can advance without an application release, after verifying help/API compatibility and render tests against that new binary. A new upstream release outside the tested catalog is reported, without presenting it as an already validated update.

Tool installs stage new version directories, preserve upstream notices, verify hashes and reported executable versions, then atomically switch the active pointer. Previous payloads are retained; a failed/cancelled install does not replace the active tool. Mutations are blocked while this app is processing audio. Automatic daily checks are opt-in; checking never installs software or uploads audio.
