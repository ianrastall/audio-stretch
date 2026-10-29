# AudioStretch development

Read D:/jupiter/AGENTS.md, the canonical stack reference and Jupiter style standard, plus README.md and HANDOFF.md. This is the original ianrastall/audio-stretch repository. Preserve its history and public URL. The owner requested fresh application code and removal of the WPF implementation; historical source is available in Git, not an active product to preserve.

- Product: a local standalone tempo utility; pitch remains unchanged. Always invoke rubberband-r3 --fine. Do not add R2 modes, mastering or YouTube features.
- Platform: Windows 11 24H2+, x64. C#/.NET 10 WinUI 3 + shared Jupiter.UI; Rust processing core; protobuf through ABI 1. Never use NumberBox.
- Source: core/, cli/, proto/, src/AudioStretch.Interop/, src/AudioStretch.App/.
- Entry points: core/src/lib.rs, cli/src/main.rs, src/AudioStretch.App/App.xaml.cs.
- Build/test/install: pwsh -NoProfile -File build.ps1 -Verify -Install. Normal builds create an installer; -AppOnly is for development.
- Native format/lint/tests: cargo fmt --all -- --check; cargo clippy --locked --all-targets --target-dir artifacts/cargo -- -D warnings; cargo test --locked --target-dir artifacts/cargo.
- Additional verification: managed/native smoke harness in tests/AudioStretch.Smoke, real audio measurement in tests/verify_audio.py.
- Generated files: protobuf bindings under obj/OUT_DIR; do not edit by hand. Build outputs are ignored under artifacts/; keep dependency lockfiles tracked.
- Prerequisites: pinned toolchains, sibling ../jupiter-ui, Inno Setup 6, existing audio tools at the build's -ToolsRoot. Keep tools local; no global installation or app runtime downloading.
- User settings: %LOCALAPPDATA%/Jupiter/AudioStretch/settings.json; preserve settings and audio during upgrades/uninstall.
- Do not delete user media or overwrite source audio. Use immutable render requests, supervised child processes and same-folder validated publication. Preserve destination on failure/cancel and reject aliases.
- Keep the source tree clean and the handoff/verification accurate. Do not claim a test or installed launch passed without evidence.
- The owner requested local installation and trial before a public release. Do not create a GitHub release until that evaluation is complete. Routine commits/pushes are authorized by Jupiter workspace policy.
