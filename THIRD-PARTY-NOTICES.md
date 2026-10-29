# Third-party software

AudioStretch invokes separate command-line tools. The local evaluation installer includes the existing tools from this machine; their names, sizes and SHA-256 hashes are recorded in `artifacts/tool-manifest.json` during build. A public release has not been published.

- Rubber Band command-line utility 4.0.0, copyright Particular Programs Ltd., GPL version 2 or later. Source and license: https://github.com/breakfastquay/rubberband/tree/v4.0.0 . Executable distribution: https://breakfastquay.com/rubberband/ . Uses the bundled libsndfile DLL.
- libsndfile: LGPL version 2.1 or later. Source and licensing: https://github.com/libsndfile/libsndfile . Retain its distribution notices when preparing public release inputs.
- FFmpeg and FFprobe 8.1, gyan.dev static full build, GPL version 3 or later for this configuration. Build/source information: https://www.gyan.dev/ffmpeg/builds/ . FFmpeg source and license: https://ffmpeg.org/releases/ and https://ffmpeg.org/legal.html . Public release packaging must include the matching corresponding source/distribution notices for the exact binaries, including enabled third-party components.
- Microsoft .NET and Windows App SDK runtimes are bundled through their standard self-contained publish. Their distribution license files remain in the published app.
- Jupiter.UI is the shared local UI library. NuGet and Cargo dependencies are recorded in their lockfiles. Prost, protox, serde_json, tempfile, same-file and windows-sys are used by the native processing core; Google.Protobuf and Grpc.Tools supply generated managed messages.

No license for the AudioStretch application itself has been selected. Do not treat these third-party licenses as an application license. Before a public binary release, resolve application licensing and package all required matching notices and corresponding-source materials.
