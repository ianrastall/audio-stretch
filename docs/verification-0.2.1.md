# AudioStretch 0.2.1 verification

29 October 2026. Release target: `v0.2.1` in the existing `ianrastall/audio-stretch` repository.

## Build and processing

`pwsh -NoProfile -File build/build.ps1 -Verify` passes: Rust formatting, Clippy with warnings denied, seven Rust tests, Release WinUI build/shared style check, native/managed integration checks and updater tests. The build produces a self-contained app installer; the separate update helper is self-contained and runs from a copied cache location outside the app directory.

The managed/native checks cover probing, exact tempo in a German numeric culture, source/output conflicts, approved and unapproved overwrite, destinations changed during rendering, missing tools, shutdown, child-process cleanup and cancellation during decode/stretch/encode. They preserve previous output and clean working files.

The same official package ZIPs users download were installed into isolated version directories by the new update service, then used through the Rust core for real rendering. The executable in the Rubber Band 4.0.0 archive matches the supplied XML's SHA-256 (`f4cd498f36e22e624268e3e5aa47803449cb786f7850b83ddd26324162fcdb8a`). FFmpeg 8.1 essentials archive hash matches its upstream GitHub release digest. Full original archives and notice files are retained locally.

Measured 6-second 440 Hz cases using the independently installed official tools:

| Change | Format | Expected duration | Measured duration | Measured frequency |
| --- | --- | --- | --- | --- |
| +25% | WAV float | 4.800000 s | 4.800000 s | 440.000009 Hz |
| -20% | FLAC 24-bit | 7.500000 s | 7.500000 s | 439.999997 Hz |
| +5.125% | WAV 16-bit | 5.707491 s | 5.707483 s | 439.999996 Hz |

96 kHz stereo 24-bit FLAC/centre-focus, MP3 input, Unicode/spaced paths and unchanged source hashes pass. These measurements establish the tested pitch/duration behavior, not perceptual quality for all recordings.

## Update checks

The update test harness covers numeric version ordering, unsupported/prerelease versions, wrong tool origins, installer checksum identity, ZIP traversal, checksum failure preserving an existing cached file, separate app/tool discovery, not offering an already-current app version, offline errors, official tool installation and retained notices, cancelled installation preserving its active pointer, and a native render through the managed tool directory.

Application checks read stable releases from the original repository. Tool checks read the versioned catalog and independently inspect the official Rubber Band Windows download page. Check failures are errors rather than false “up-to-date” results. New upstream versions not in the tested catalog are reported separately. Automatic daily checks are optional/off by default and never install software.

Public installation differs from local-only 0.2.0: audio tools download directly from upstream on explicit setup, rather than being redistributed inside the AudioStretch installer. The installer includes managed runtimes, update helper, catalog and the XML reference. Existing bundled tools remain a fallback. Settings/audio and separate tool installations survive upgrades/uninstall.

## Release and live acceptance

The release publishes an installer, SHA256SUMS.txt and a source snapshot containing the exact sibling Jupiter.UI source dependency used by the build. The original repository/history/URL remain intact. The old local-only 0.2.0 installer is excluded from public assets.

A controlled external-helper check passed before publication: the self-contained helper ran from the user update cache, remained waiting while the controlled parent process was alive, installed 0.2.1 with exit code 0, preserved the settings hash and relaunched AudioStretch. `artifacts/helper-verification.json` records installed version 0.2.1.0 and success.

[Release v0.2.1](https://github.com/ianrastall/audio-stretch/releases/tag/v0.2.1) was published as the latest stable release at commit `5cb1b9465787863affb4474b2126d7980fa0887c`. Uploaded installer and source ZIP digests match SHA256SUMS.txt. The complete 99-file source ZIP layout and a locked restore from its extracted sibling project directories pass.

The live update service discovered that public release as an upgrade from 0.2.0 and validated the installer cache against its published SHA-256. It read the official upstream Rubber Band version as 4.0.0 and downloaded/installed the catalog's official Rubber Band and FFmpeg archives into this account's managed tool directory, preserving notices. These results are in `artifacts/live-update-verification.json`. The application remains installed at 0.2.1.0; existing settings were preserved.

## Limits

The installer is unsigned. Full pitch/duration modes and map editing are planned in `full-r3-frontend.md`, not implemented in 0.2.1. Windows playback codec availability varies. Tool updates are accepted only from the tested catalog; the catalog must be advanced after compatibility testing for each new upstream release. No Windows contrast-theme or comprehensive screen-reader audit was performed. See existing decisions for destination check/move race and forced-termination working-folder cleanup limits.
