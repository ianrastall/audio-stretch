mod process;
pub mod wire {
    include!(concat!(env!("OUT_DIR"), "/audio.rs"));
}

use prost::Message;
use std::{
    fs,
    path::{Path, PathBuf},
    sync::{
        Arc, Mutex,
        atomic::{AtomicBool, Ordering},
    },
    thread::{self, JoinHandle},
    time::{Duration, Instant, SystemTime},
};
use wire::{AudioInfo, Reply, Request, Snapshot};

pub fn multiplier(change: f64) -> Result<f64, String> {
    if !change.is_finite() || !(-75.0..=300.0).contains(&change) {
        return Err("Tempo change must be between -75% and +300%.".into());
    }
    Ok(1.0 + change / 100.0)
}

fn value_u32(value: &serde_json::Value) -> u32 {
    value
        .as_u64()
        .and_then(|v| u32::try_from(v).ok())
        .or_else(|| value.as_str()?.parse().ok())
        .unwrap_or(0)
}
fn duration(value: &serde_json::Value) -> f64 {
    value
        .as_f64()
        .or_else(|| value.as_str()?.parse().ok())
        .filter(|x| x.is_finite() && *x > 0.0)
        .unwrap_or(0.0)
}
pub fn parse_probe(json: &str) -> Result<AudioInfo, String> {
    let value: serde_json::Value =
        serde_json::from_str(json).map_err(|e| format!("Invalid audio probe: {e}"))?;
    let stream = value["streams"]
        .as_array()
        .and_then(|s| s.first())
        .ok_or("The file has no audio stream.")?;
    let codec = stream["codec_name"].as_str().unwrap_or("").to_owned();
    let sample_rate = value_u32(&stream["sample_rate"]);
    let channels = value_u32(&stream["channels"]);
    if !(8000..=192000).contains(&sample_rate) || !(1..=8).contains(&channels) {
        return Err("Supported audio is 8–192 kHz with 1–8 channels. No resampling or channel conversion was performed.".into());
    }
    let stream_duration = duration(&stream["duration"]);
    let secs = if stream_duration > 0.0 {
        stream_duration
    } else {
        duration(&value["format"]["duration"])
    };
    if secs <= 0.0 {
        return Err("Could not determine the audio duration.".into());
    }
    let bit_depth =
        value_u32(&stream["bits_per_raw_sample"]).max(value_u32(&stream["bits_per_sample"]));
    let lossless = codec.starts_with("pcm_")
        || ["flac", "alac", "wavpack", "ape", "tta", "truehd", "mlp"].contains(&codec.as_str());
    Ok(AudioInfo {
        sample_rate,
        channels,
        bit_depth,
        codec,
        duration: secs,
        lossy: !lossless,
    })
}

#[derive(Clone)]
pub struct Tools {
    directory: PathBuf,
}
impl Tools {
    pub fn new(directory: impl Into<PathBuf>) -> Self {
        Self {
            directory: directory.into(),
        }
    }
    fn exe(&self, name: &str) -> Result<PathBuf, String> {
        let path = self.directory.join(format!("{name}.exe"));
        if !path.is_file() {
            return Err(format!(
                "Missing {name}. Reinstall AudioStretch to restore its audio tools."
            ));
        }
        Ok(path)
    }
    pub fn probe(&self, input: &Path, cancel: &AtomicBool) -> Result<AudioInfo, String> {
        if !input.is_file() {
            return Err("The input audio file does not exist.".into());
        }
        let text = process::run(
            &self.exe("ffprobe")?,
            &strings(&[
                "-v",
                "error",
                "-select_streams",
                "a:0",
                "-show_streams",
                "-show_format",
                "-of",
                "json",
                &input.to_string_lossy(),
            ]),
            cancel,
            Duration::from_secs(30),
        )?;
        parse_probe(&text)
    }
}
fn strings(args: &[&str]) -> Vec<String> {
    args.iter().map(|s| (*s).into()).collect()
}

#[derive(Debug, PartialEq)]
struct Fingerprint {
    length: u64,
    modified: SystemTime,
    identity: same_file::Handle,
}
fn fingerprint(path: &Path) -> Result<Option<Fingerprint>, String> {
    use std::os::windows::fs::OpenOptionsExt;
    match fs::metadata(path) {
        Ok(m) => {
            if !m.is_file() {
                return Err("The output path is not a regular file.".into());
            }
            Ok(Some(Fingerprint {
                length: m.len(),
                modified: m.modified().map_err(|e| e.to_string())?,
                // Keep identity live without blocking the eventual atomic replacement.
                identity: same_file::Handle::from_file(
                    fs::OpenOptions::new()
                        .read(true)
                        .share_mode(7)
                        .open(path)
                        .map_err(|e| e.to_string())?,
                )
                .map_err(|e| e.to_string())?,
            }))
        }
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(None),
        Err(e) => Err(e.to_string()),
    }
}

fn validate_paths(
    input: &Path,
    output: &Path,
    overwrite: bool,
) -> Result<Option<Fingerprint>, String> {
    if !input.is_file() {
        return Err("The input audio file does not exist.".into());
    }
    if !output.is_absolute() {
        return Err("Choose an absolute output path.".into());
    }
    if output.exists() && same_file::is_same_file(input, output).map_err(|e| e.to_string())? {
        return Err(
            "The output is the source audio file or an alias of it. Choose a different file."
                .into(),
        );
    }
    if !output.parent().is_some_and(Path::is_dir) {
        return Err("The output folder does not exist.".into());
    }
    let before = fingerprint(output)?;
    if before.is_some() && !overwrite {
        return Err(
            "The output already exists. Choose another name or explicitly replace it.".into(),
        );
    }
    Ok(before)
}

fn log(snapshot: &Arc<Mutex<Snapshot>>, text: &str) {
    let mut s = snapshot.lock().unwrap();
    s.log.push_str(text);
    s.log.push('\n');
    if s.log.len() > 65536 {
        let mut start = s.log.len() - 65536;
        while !s.log.is_char_boundary(start) {
            start += 1;
        }
        s.log.drain(..start);
    }
}
fn stage(snapshot: &Arc<Mutex<Snapshot>>, name: &str) {
    snapshot.lock().unwrap().stage = name.into();
    log(snapshot, name);
}

pub fn render(
    tools: &Tools,
    request: &Request,
    cancel: &AtomicBool,
    snapshot: &Arc<Mutex<Snapshot>>,
) -> Result<(), String> {
    let speed = multiplier(request.tempo_change)?;
    let input = PathBuf::from(&request.input);
    let output = PathBuf::from(&request.output);
    let before = validate_paths(&input, &output, request.overwrite)?;
    let ext = output
        .extension()
        .and_then(|s| s.to_str())
        .unwrap_or("")
        .to_ascii_lowercase();
    let encoding = request.encoding.as_str();
    if !matches!(
        (ext.as_str(), encoding),
        ("wav", "float" | "24" | "16") | ("flac", "24" | "16")
    ) {
        return Err(
            "Choose WAV (32-bit float, 24-bit or 16-bit) or FLAC (24-bit or 16-bit).".into(),
        );
    }
    let rb = tools.exe("rubberband-r3")?;
    let ffmpeg = tools.exe("ffmpeg")?;
    stage(snapshot, "Probing audio");
    let info = tools.probe(&input, cancel)?;
    log(
        snapshot,
        &format!(
            "{} Hz · {} channels · {} · {:.3}s\nTempo: {speed:.9}x · pitch unchanged · R3/Finer",
            info.sample_rate, info.channels, info.codec, info.duration
        ),
    );
    let work = tempfile::Builder::new()
        .prefix(".audiostretch-")
        .tempdir_in(output.parent().unwrap())
        .map_err(|e| format!("Cannot create working audio in the output folder: {e}"))?;
    let decoded = work.path().join("decoded.wav");
    let stretched = work.path().join("stretched.wav");
    let pending = work.path().join(format!("result.{ext}"));
    let timeout = Duration::from_secs(86400);
    stage(snapshot, "Decoding to floating-point audio");
    let text = process::run(
        &ffmpeg,
        &strings(&[
            "-hide_banner",
            "-v",
            "error",
            "-nostdin",
            "-y",
            "-i",
            &request.input,
            "-map",
            "0:a:0",
            "-vn",
            "-sn",
            "-dn",
            "-map_metadata",
            "-1",
            "-c:a",
            "pcm_f32le",
            "-rf64",
            "auto",
            &decoded.to_string_lossy(),
        ]),
        cancel,
        timeout,
    )?;
    log(snapshot, &text);
    stage(snapshot, "Stretching with Rubber Band R3/Finer");
    let mut args = strings(&["--fine", "--tempo", &speed.to_string()]);
    if request.centre_focus && info.channels == 2 {
        args.push("--centre-focus".into());
    }
    args.extend([
        decoded.to_string_lossy().into_owned(),
        stretched.to_string_lossy().into_owned(),
    ]);
    log(
        snapshot,
        &format!(
            "rubberband-r3 --fine --tempo {speed}{}",
            if request.centre_focus && info.channels == 2 {
                " --centre-focus"
            } else {
                ""
            }
        ),
    );
    log(snapshot, &process::run(&rb, &args, cancel, timeout)?);
    stage(snapshot, "Encoding output");
    let mut args = strings(&[
        "-hide_banner",
        "-v",
        "error",
        "-nostdin",
        "-y",
        "-i",
        &stretched.to_string_lossy(),
        "-map",
        "0:a:0",
        "-map_metadata",
        "-1",
    ]);
    if ext == "flac" {
        args.extend(strings(&[
            "-c:a",
            "flac",
            "-compression_level",
            "8",
            "-sample_fmt",
            if encoding == "16" { "s16" } else { "s32" },
            "-bits_per_raw_sample",
            encoding,
        ]));
    } else {
        args.extend(strings(&[
            "-c:a",
            match encoding {
                "16" => "pcm_s16le",
                "24" => "pcm_s24le",
                _ => "pcm_f32le",
            },
            "-rf64",
            "auto",
        ]));
    }
    if encoding != "float" {
        args.extend(strings(&[
            "-af",
            &format!("aresample=output_sample_bits={encoding}:dither_method=triangular"),
        ]));
    }
    args.push(pending.to_string_lossy().into_owned());
    log(snapshot, &process::run(&ffmpeg, &args, cancel, timeout)?);
    stage(snapshot, "Validating output");
    let result = tools.probe(&pending, cancel)?;
    let expected = info.duration / speed;
    if result.sample_rate != info.sample_rate
        || result.channels != info.channels
        || (result.duration - expected).abs() > 0.1_f64.max(expected * 0.0001)
    {
        return Err("Output validation failed: duration, sample rate or channels differ from the processing plan. The destination was preserved.".into());
    }
    if cancel.load(Ordering::Acquire) {
        return Err("Cancelled".into());
    }
    if fingerprint(&output)? != before {
        return Err("The destination changed during rendering. It was preserved; choose another output name and run again.".into());
    }
    // The source is never opened for writing; the destination is published only after validation.
    let overwrite = before.is_some();
    // Windows replacement needs the compared destination handles released first.
    drop(before);
    publish(&pending, &output, overwrite)?;
    snapshot.lock().unwrap().output = request.output.clone();
    log(
        snapshot,
        &format!("Completed: {} ({:.3}s)", output.display(), result.duration),
    );
    Ok(())
}

fn publish(source: &Path, target: &Path, overwrite: bool) -> Result<(), String> {
    use std::os::windows::ffi::OsStrExt;
    #[link(name = "kernel32")]
    unsafe extern "system" {
        fn MoveFileExW(source: *const u16, target: *const u16, flags: u32) -> i32;
    }
    let src: Vec<_> = source.as_os_str().encode_wide().chain(Some(0)).collect();
    let dst: Vec<_> = target.as_os_str().encode_wide().chain(Some(0)).collect();
    // Same-volume move, with no replace flag unless replacement was explicitly approved.
    if unsafe { MoveFileExW(src.as_ptr(), dst.as_ptr(), u32::from(overwrite) | 8) } == 0 {
        return Err(std::io::Error::last_os_error().to_string());
    }
    Ok(())
}

pub struct Engine {
    tools: Tools,
    snapshot: Arc<Mutex<Snapshot>>,
    cancel: Arc<AtomicBool>,
    worker: Option<JoinHandle<()>>,
    started: Option<Instant>,
}
impl Engine {
    pub fn new(directory: impl Into<PathBuf>) -> Self {
        Self {
            tools: Tools::new(directory),
            snapshot: Arc::new(Mutex::new(Snapshot {
                state: "Queued".into(),
                ..Default::default()
            })),
            cancel: Arc::new(AtomicBool::new(false)),
            worker: None,
            started: None,
        }
    }
    pub fn call(&mut self, req: Request) -> Reply {
        let mut reply = Reply {
            request_id: req.request_id,
            ..Default::default()
        };
        let operation = || -> Result<Option<AudioInfo>, String> {
            match req.operation.as_str() {
                "probe" => Ok(Some(
                    self.tools
                        .probe(Path::new(&req.input), &AtomicBool::new(false))?,
                )),
                "start" => {
                    if self.worker.as_ref().is_some_and(|w| !w.is_finished()) {
                        return Err("Audio processing is already running.".into());
                    }
                    if let Some(w) = self.worker.take() {
                        let _ = w.join();
                    }
                    multiplier(req.tempo_change)?;
                    self.cancel.store(false, Ordering::Release);
                    *self.snapshot.lock().unwrap() = Snapshot {
                        state: "Running".into(),
                        stage: "Starting".into(),
                        ..Default::default()
                    };
                    let tools = self.tools.clone();
                    let cancel = self.cancel.clone();
                    let snapshot = self.snapshot.clone();
                    self.started = Some(Instant::now());
                    self.worker = Some(thread::spawn(move || {
                        let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| render(&tools, &req, &cancel, &snapshot)))
                            .unwrap_or_else(|_| Err("The audio worker stopped unexpectedly. The destination was preserved.".into()));
                        let mut s = snapshot.lock().unwrap_or_else(|p| p.into_inner());
                        match result {
                            Ok(()) => {
                                s.state = "Completed".into();
                                s.stage = "Completed".into();
                            }
                            Err(e) if e == "Cancelled" => {
                                s.state = "Cancelled".into();
                                s.stage = "Cancelled".into();
                            }
                            Err(e) => {
                                s.state = "Failed".into();
                                s.stage = "Failed".into();
                                s.error = e;
                            }
                        }
                    }));
                    Ok(None)
                }
                "cancel" => {
                    if self.worker.as_ref().is_some_and(|w| !w.is_finished()) {
                        self.cancel.store(true, Ordering::Release);
                        let mut s = self.snapshot.lock().unwrap();
                        if s.state == "Running" {
                            s.state = "Cancelling".into();
                        }
                    }
                    Ok(None)
                }
                "poll" => Ok(None),
                _ => Err("Unknown core operation.".into()),
            }
        };
        match operation() {
            Ok(info) => reply.info = info,
            Err(e) => reply.error = e,
        }
        let mut s = self.snapshot.lock().unwrap();
        if matches!(s.state.as_str(), "Running" | "Cancelling") {
            s.elapsed = self
                .started
                .map(|t| t.elapsed().as_secs_f64())
                .unwrap_or(0.0);
        }
        reply.snapshot = Some(s.clone());
        reply
    }
}
impl Drop for Engine {
    fn drop(&mut self) {
        self.cancel.store(true, Ordering::Release);
        if let Some(w) = self.worker.take() {
            let _ = w.join();
        }
    }
}

// ABI 1: handles are opaque, owned by caller until free; all calls on one Engine must be serialized.
// Byte pointers are borrowed for call duration. Result bytes remain valid until result_free.
#[unsafe(no_mangle)]
pub extern "C" fn audio_abi_version() -> u32 {
    1
}
/// # Safety
/// `path` addresses `len` bytes; `out` is writable. Caller owns the returned engine.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn audio_create(path: *const u8, len: usize, out: *mut *mut Engine) -> i32 {
    if path.is_null() || out.is_null() || len > 32768 {
        return 1;
    }
    unsafe {
        *out = std::ptr::null_mut();
    }
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        let Ok(path) = std::str::from_utf8(unsafe { std::slice::from_raw_parts(path, len) }) else {
            return 1;
        };
        unsafe {
            *out = Box::into_raw(Box::new(Engine::new(path)));
        }
        0
    }))
    .unwrap_or(2)
}
/// # Safety
/// Handles must be live; input addresses `len` readable bytes and `out` is writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn audio_call(
    engine: *mut Engine,
    input: *const u8,
    len: usize,
    out: *mut *mut Vec<u8>,
) -> i32 {
    if engine.is_null() || input.is_null() || out.is_null() || len > 1_048_576 {
        return 1;
    }
    unsafe {
        *out = std::ptr::null_mut();
    }
    std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
        let Ok(req) = Request::decode(unsafe { std::slice::from_raw_parts(input, len) }) else {
            return 1;
        };
        let bytes = unsafe { &mut *engine }.call(req).encode_to_vec();
        unsafe {
            *out = Box::into_raw(Box::new(bytes));
        }
        0
    }))
    .unwrap_or(2)
}
/// # Safety
/// Result is a live result handle; `data` and `len` are writable.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn audio_result_bytes(
    result: *const Vec<u8>,
    data: *mut *const u8,
    len: *mut usize,
) -> i32 {
    if result.is_null() || data.is_null() || len.is_null() {
        return 1;
    }
    unsafe {
        *data = (*result).as_ptr();
        *len = (*result).len();
    }
    0
}
/// # Safety
/// Free a result exactly once; no subsequent pointer access is allowed.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn audio_result_free(result: *mut Vec<u8>) {
    if !result.is_null() {
        unsafe {
            drop(Box::from_raw(result));
        }
    }
}
/// # Safety
/// Destroy a live engine exactly once with no concurrent calls.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn audio_destroy(engine: *mut Engine) {
    if !engine.is_null() {
        unsafe {
            drop(Box::from_raw(engine));
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn speed_and_duration() {
        assert_eq!(multiplier(5.0).unwrap(), 1.05);
        assert_eq!(multiplier(-50.0).unwrap(), 0.5);
        assert!((120.0 / multiplier(25.0).unwrap() - 96.0).abs() < 1e-9);
    }
    #[test]
    fn invalid_tempos() {
        for value in [f64::NAN, f64::INFINITY, -100.0, -75.01, 300.01] {
            assert!(multiplier(value).is_err());
        }
    }
    #[test]
    fn unknown_precision_stays_unknown() {
        let info = parse_probe(r#"{"streams":[{"sample_rate":"48000","channels":2,"codec_name":"alac"}],"format":{"duration":"12.5"}}"#).unwrap();
        assert_eq!(info.bit_depth, 0);
        assert!(!info.lossy);
        assert_eq!(info.duration, 12.5);
    }
    #[test]
    fn unsupported_audio_is_rejected() {
        assert!(
            parse_probe(
                r#"{"streams":[{"sample_rate":"384000","channels":2}],"format":{"duration":"1"}}"#
            )
            .is_err()
        );
        assert!(parse_probe(r#"{"streams":[]}"#).is_err());
    }
    #[test]
    fn source_hardlinks_and_output_conflicts() {
        let dir = tempfile::tempdir().unwrap();
        let source = dir.path().join("source.wav");
        let output = dir.path().join("alias.wav");
        fs::write(&source, b"original").unwrap();
        fs::hard_link(&source, &output).unwrap();
        assert!(validate_paths(&source, &output, true).is_err());
        fs::remove_file(&output).unwrap();
        fs::write(&output, b"existing").unwrap();
        assert!(validate_paths(&source, &output, false).is_err());
        assert!(validate_paths(&source, &output, true).is_ok());
    }
    #[test]
    fn changed_destination_is_detected() {
        let dir = tempfile::tempdir().unwrap();
        let output = dir.path().join("out.wav");
        fs::write(&output, b"before").unwrap();
        let before = fingerprint(&output).unwrap();
        fs::write(&output, b"changed contents").unwrap();
        assert_ne!(before, fingerprint(&output).unwrap());
    }
    #[test]
    fn destination_publish_replaces_only_when_approved() {
        let dir = tempfile::tempdir().unwrap();
        let pending = dir.path().join("pending");
        let output = dir.path().join("output");
        fs::write(&pending, b"new").unwrap();
        fs::write(&output, b"old").unwrap();
        assert!(publish(&pending, &output, false).is_err());
        assert_eq!(fs::read(&output).unwrap(), b"old");
        let before = fingerprint(&output).unwrap();
        assert_eq!(fingerprint(&output).unwrap(), before);
        drop(before);
        publish(&pending, &output, true).unwrap();
        assert_eq!(fs::read(&output).unwrap(), b"new");
    }
}
