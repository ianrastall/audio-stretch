# Third-party software

AudioStretch invokes separate Rubber Band and FFmpeg command-line tools. Public AudioStretch installers do not redistribute those audio binaries. When the user selects installation in Settings, the app downloads complete official upstream archives, verifies the catalog SHA-256 and executable versions, and preserves their notices alongside the installed payload. Original downloaded archives remain in the local update cache. The program does not link the Rubber Band or FFmpeg libraries into its own processing core.

- Rubber Band Windows utility 4.0.0: copyright Particular Programs Ltd.; GPL version 2 or later. Official archive: https://breakfastquay.com/files/releases/rubberband-4.0.0-gpl-executable-windows.zip . Source and license: https://github.com/breakfastquay/rubberband/tree/v4.0.0 . The archive includes the companion libsndfile DLL and COPYING/README files.
- libsndfile: LGPL version 2.1 or later. Source/licensing: https://github.com/libsndfile/libsndfile . Supplied by the official Rubber Band executable archive; its distributor's notices remain intact.
- FFmpeg/FFprobe 8.1 essentials build by Gyan Doshi: GPL version 3 or later for that upstream build. Official distribution and source/build information: https://www.gyan.dev/ffmpeg/builds/ and https://github.com/GyanD/codexffmpeg/releases/tag/8.1 . The archive's LICENSE and README files are retained by setup.
- Microsoft .NET and Windows App SDK runtimes are included through standard self-contained publishing. Their published runtime license/notice files remain in the installation. Package identities are recorded in NuGet lockfiles.
- Jupiter.UI is the shared UI source by Ian Rastall. The source-release ZIP includes the exact shared working snapshot used to build this release without modifying the shared workspace.
- Rust dependencies: prost/prost-build, protox, serde/serde_json, sha2, tempfile, same-file and windows-sys, plus their transitive dependencies, recorded in Cargo.lock. Managed protobuf uses Google.Protobuf/Grpc.Tools, recorded in the managed lockfiles. The updater uses the .NET base class libraries and adds no third-party network/archive dependency.

The earlier 0.2.0 local-only installer bundled pre-existing audio binaries from this machine. It is not a public release asset. Its files remain usable on upgrades; user-installed tools and their retained notices are preferred by later renders.

No additional license for the AudioStretch application source has been selected. This notice does not relicense upstream tools or grant an application source license. Users obtain upstream audio packages under their distributors' terms.
