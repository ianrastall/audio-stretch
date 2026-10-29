# AudioStretch 0.3.0 verification

29 October 2026. Release target: v0.3.0 in the existing ianrastall/audio-stretch repository.

## Automated checks

`build/build.ps1 -Verify` passes Rust formatting, Clippy with warnings denied, 11 Rust tests, locked restore, WinUI Release/shared style checks, managed/native integration, updater checks, legacy audio/format checks and 22 measured full-R3 cases. Final presentation changes were rebuilt/installed with `build/build.ps1 -Install`. Public publishing checks reject unintended audio-tool payloads.

Integration covers German numeric culture, job roundtrip/source identity/maps/overwrite reset, time-entry conversion, approved/unapproved overwrite, source aliases, destination mutation, source mutation during mapped rendering, missing tools and shutdown. Cancellation during decode, mapped R3 processing and encoding preserves previous output, cleans working audio and leaves no child tool. Source mutation publishes neither audio nor a success receipt.

Updater checks retain version ordering, package origins, ZIP traversal, checksum/cache preservation, independent app/tool discovery, offline errors, official package installation/notices, cancellation preserving active tools and an actual managed-tool render. Catalog: Rubber Band 4.0.0 and FFmpeg 8.1 essentials.

## Measured R3 behavior

`tests/verify_r3.py` uses only Python's standard library and real tools. Receipt hashes are checked against actual files. Raw evidence: ignored `artifacts/r3-verification.json`.

| Case | Requested | Measured |
| --- | --- | --- |
| +12 semitones | 880 Hz, 6 s | 879.999995 Hz, 6 s |
| -12 semitones | 220 Hz, 6 s | 219.999975 Hz, 6 s |
| +700 cents | 659.255114 Hz | 659.255117 Hz |
| Frequency 1.5 | 660 Hz | 659.999966 Hz |
| BPM 120:150 | 4.8 s, 440 Hz | 4.8 s, 440.000001 Hz |
| Duration multiplier 1.25 | 7.5 s | 7.5 s |
| Target duration | 4.2 s | 4.2 s |
| Speed 1.25 | 4.8 s | 4.8 s |
| Combined duration/+12 | 5 s, 880 Hz | 5 s, 879.999980 Hz |
| Real-time HQ/+3 | 523.251131 Hz | 523.251132 Hz |
| Draft/+20%, debug/quiet | 5 s, 440 Hz | 5 s, 440.000116 Hz |
| Pitch map | 440 → 880 → 220 Hz | 439.999696 → 880.000095 → 219.999917 Hz |
| Frequency map + initial +3 | 523.251131 → 1046.502261 → 261.625565 Hz | 523.251118 → 1046.502332 → 261.625598 Hz |

Time-map bursts requested at 0.75, 3.25 and 5.5 seconds peak at 0.751134, 3.247937 and 5.500771 seconds. Testing exposed R3 4.0.0's zero division when a native map begins with `0 0`; normalized maps omit this implicit origin, with a unit regression. Final anchors are checked/added against aggregate duration.

A synthesized voiced vowel (100 Hz harmonics shaped by resonant bands at 700/1300 Hz) confirms formant preservation changes the spectral envelope: original-band energy relative to shifted upper bands increases substantially. This assesses a voiced spectrum rather than inferring formants from a sine; it is not a listening-quality score for real vocals/music.

A loud float source confirms reduced-gain recovery (logged restart gain 0.829512) and bounded output. Clamp mode produces bounded output without recovery messages. Debug 3 executes with explicit audible-tick opt-in/warnings. Normal/full help accepts the tool's deliberate status 2.

Receipt replay preserves embedded maps after external map edits; changed sources are rejected. Invalid/non-finite ratios, unapproved debug 3, offline pitch-HQ, duplicate/unordered/out-of-range/malformed maps and time-map/real-time combinations fail without audio publication. Legacy checks retain WAV float/16/24, FLAC 24, 96 kHz stereo/centre focus, MP3 input, Unicode paths and unchanged pitch.

## Installed GUI

The per-user installer upgrades to 0.3.0.0 with the existing AppId, registered uninstaller and Start Menu entry. Preferences were unchanged byte-for-byte across installation. The Start Menu shortcut launches. Computer-use verification covered source loading, editable source-bound rows, normalized map preview/Apply, GUI mapped export, receipt saving through the Windows picker, F1 supplied reference and actual installed full help. GUI output is 5.714285714 seconds with effectively zero error; its receipt contains the matching actual output hash/map. No new error-log entries occurred. Test preferences were restored byte-for-byte and the app reopened ready for use.

## Limits

Offline duration tolerance: max(0.1 seconds, 0.01%); real-time: 0.5 seconds + 2%. Actual error is always reported. Anchors are approximate. CLI source/output/map cap: 2,147,483,647 sample frames; 10,000 rows per family. Compressed input is decoded during probing for accurate gapless sample counts. Replay uses current tools and exposes hashes; identical output across versions is not promised.

Unsigned binaries, Windows-dependent preview codecs, omitted tags/artwork and crash/forced-termination temporary-folder limits remain. Checks never install automatically; daily checks are opt-in. No audio is uploaded. Synthetic acceptance does not establish listening quality on every song.
