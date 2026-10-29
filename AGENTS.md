# AudioStretch development

Read D:/jupiter/AGENTS.md, the canonical stack/style standards, README.md and HANDOFF.md. Preserve the original ianrastall/audio-stretch repository and public URL. WPF was intentionally removed; its history is not an active product requirement.

- The owner authorized publishing a release now, then designing a complete Rubber Band R3 frontend using docs/rubberband-r3-help.xml. The earlier hold on public releases is superseded. Keep the current tempo workflow as the default; pitch/maps are planned capabilities, not implemented controls.
- Always use R3/Finer by default. R2-only/no-op switches must not become misleading R3 controls. No mastering/YouTube scope.
- Windows 11 24H2+, x64; WinUI 3/C# + canonical Jupiter.UI; Rust audio core; generated protobuf / SafeHandle ABI 1. Never use NumberBox.
- Build: pwsh -NoProfile -File build.ps1. Normal builds produce an installer; -AppOnly is development only. -Verify adds Rust checks, managed/native/update tests and measured audio tests. -Install installs locally. Dependencies/toolchains and lockfiles are pinned.
- Audio tools are obtained directly from official upstream archives by explicit setup actions. Automatic update checks are opt-in, never automatic installation. Do not add telemetry or upload audio. Preserve settings/audio and active tools on failure/cancel.
- Update catalog: updates/tool-catalog.json. Check exact package origins, SHA-256, versions and processing compatibility before advancing it. Report upstream versions outside the catalog rather than claiming they are already validated.
- Application updates come from this repository's stable releases with verified installer checksums. The self-contained helper runs outside the install directory and waits for the app to exit. Keep failures visible and preserve data.
- Source: core/, cli/, proto/, src/, build/, tests/. Never hand-edit generated protobuf. Ignore artifacts/ and work/.
- User data: %LOCALAPPDATA%/Jupiter/AudioStretch. Keep settings, tool packages, notices and user audio on upgrade/uninstall. Never globally install tools or change PATH.
- No code or build changes to unrelated shared workspaces. Source-release packaging may include the exact shared Jupiter.UI source snapshot as a sibling dependency, with file hashes, without committing its unrelated changes.
- Use build/package-source.ps1 after committing release source; publish the installer, source snapshot and SHA256SUMS.txt. Do not publish the old local-only 0.2.0 installer.
- Record actual verification and remaining limits; do not equate synthetic tone checks with listening quality on every song.
