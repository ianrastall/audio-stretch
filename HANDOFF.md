# AudioStretch handoff

29 October 2026.

## Current state

This is the original `ianrastall/audio-stretch` repository, cloned with its history into `D:/jupiter/audio-stretch`. `origin` retains the original GitHub URL. `master` and `legacy-wpf` point to WPF head `a973c43`; the `jupiter` branch contains the rebuild planning records. No new application implementation or installer exists yet. The existing WPF files remain unchanged.

The owner explicitly wants fresh code, repository continuity, tempo changes with pitch preserved, and Rubber Band R3 at the finest setting. Do not create a replacement GitHub repository. See [the confirmed direction and proposed implementation](docs/jupiter-project-brief.md) and [the source audit](docs/legacy-audit.md).

## Work completed

- Read the Jupiter workspace instructions and stack reference, the repository instructions/readme/build/project files and application source.
- Identified the repository through GitHub, fetched its metadata and root listing, and cloned full history.
- Inspected the existing SongLab documentation to distinguish its mastering scope from this standalone utility.
- Checked the installed Rubber Band help and official upstream documentation: `--fine` selects R3; R2 crispness is not an additional R3 quality setting.
- Created `jupiter` and `legacy-wpf` branches, leaving the default branch unchanged.
- Added this handoff and `docs/jupiter-project-brief.md` / `docs/legacy-audit.md`. Added a branch-status note to the readme.

## Verification

Commands included `git clone`, `git log -5 --oneline`, `git branch -a`, `git remote -v`, `git status --short`, `git diff --check`, and `D:/app/rubberband/rubberband-r3.exe --help`. Read-only GitHub metadata and traffic queries also succeeded. The old application was not built or run, and no new GUI is claimed delivered. Validate branch ancestry and clean status after committing the planning records.

## Next implementation work

Read the full binding Jupiter style standard and Jupiter.UI integration instructions before UI work. Settle export formats/precision/metadata and stereo centre-focus defaults. Build a fresh Rust processing core and CLI first, proving floating-point decode, offline R3 stretch, safe destination commit and measured pitch/duration behavior. Then add the generated native boundary and WinUI/Jupiter.UI shell. Finish with an installed, verified Windows release in the same repository.

Keep the legacy branch available throughout. Do not delete tracked sample media, invent a license, publish an unverified replacement to the default branch, or equate this planning pass with a finished application.
