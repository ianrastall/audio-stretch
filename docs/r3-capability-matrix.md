# Rubber Band R3 capability matrix

Generated from the supplied XML manual, product version 4.0.0. Its executable SHA-256 matches the executable in the official Windows ZIP fetched for this release. Source XML option IDs and detailed interactions remain authoritative reference data; the application must validate them against each supported binary version.

Executable SHA-256: `f4cd498f36e22e624268e3e5aa47803449cb786f7850b83ddd26324162fcdb8a`.

| CLI option | Frontend status | Meaning |
| --- | --- | --- |
| `--time` | Planned R3 support | Stretch to RATIO times the original duration. |
| `--tempo` | Implemented | Change tempo by MULTIPLIER, or from X to Y. |
| `--duration` | Planned R3 support | Make the output approximately SECONDS long. |
| `--pitch` | Planned R3 support | Raise pitch by SEMITONES; negative values lower it. |
| `--frequency` | Planned R3 support | Multiply frequencies by RATIO. |
| `--timemap` | Planned R3 support | Read a source-frame to target-frame key map from FILE. |
| `--pitchmap` | Planned R3 support | Read input-frame and semitone-offset pairs from FILE. |
| `--freqmap` | Planned R3 support | Read input-frame and frequency-multiplier pairs from FILE. |
| `--fast` | Reference only: R2 outside this frontend | Use the lower-CPU R2 (Faster) engine. |
| `--fine` | Implemented | Use the higher-quality, higher-CPU R3 (Finer) engine. |
| `--formant` | Planned R3 support | Preserve the formant envelope during pitch shifting. |
| `--centre-focus` | Implemented | Process two-channel audio together to preserve stereo center focus. |
| `--crisp` | Reference only: R2 outside this frontend | Select an R2 crispness preset. |
| `--realtime` | Planned R3 support | Select the stretcher's real-time processing mode. |
| `--no-threads` | Reference only: R2 outside this frontend | Never use extra processing threads. |
| `--threads` | Reference only: R2 outside this frontend | Assume multiple CPUs and always allow extra threads. |
| `--no-transients` | Reference only: R2 outside this frontend | Use smooth transient handling instead of crisp phase resynchronization. |
| `--bl-transients` | Reference only: R2 outside this frontend | Band-limit transient phase resynchronization to extreme frequencies. |
| `--no-lamination` | Reference only: R2 outside this frontend | Disable phase lamination and use independent phase handling. |
| `--smoothing` | Reference only: R2 outside this frontend | Apply window presum and time-domain smoothing. |
| `--detector-perc` | Reference only: R2 outside this frontend | Use the percussive transient detector. |
| `--detector-soft` | Reference only: R2 outside this frontend | Use the soft transient detector. |
| `--window-long` | Reference only: R2 outside this frontend | Use a longer processing window. |
| `--window-short` | Planned R3 support | Use a shorter processing window. |
| `--pitch-hq` | Planned R3 support | In real-time mode, use slower high-quality pitch shifting. |
| `--ignore-clipping` | Planned R3 support | Clamp clipped output samples instead of restarting at reduced gain. |
| `--loose` | Reference only: accepted no-op | Compatibility option; accepted and ignored. |
| `--precise` | Reference only: accepted no-op | Compatibility option; accepted and ignored. |
| `--debug` | Planned R3 support | Select the library debug level. |
| `--quiet` | Planned R3 support | Suppress progress and most informational output. |
| `--version` | Tool information/help | Print 4.0.0 to standard error and exit successfully. |
| `--help` | Tool information/help | Print normal help to standard error. |
| `--full-help` | Tool information/help | Print all options and crispness presets to standard error. |

“Full R3 frontend” means all meaningful R3 processing capabilities, including dynamic maps. It does not mean presenting R2-only options as effective R3 controls. Administration switches become help/tool information, while ignored compatibility switches stay documented rather than becoming enabled controls.
