use audio_stretch_core::{
    Engine,
    wire::{MapKind, PitchMode, Request, TimeMode, Transform},
};
use std::{
    path::Path,
    sync::atomic::{AtomicBool, Ordering},
};
static CANCEL: AtomicBool = AtomicBool::new(false);
#[link(name = "kernel32")]
unsafe extern "system" {
    fn SetConsoleCtrlHandler(
        handler: Option<unsafe extern "system" fn(u32) -> i32>,
        add: i32,
    ) -> i32;
}
unsafe extern "system" fn interrupt(signal: u32) -> i32 {
    if signal <= 1 {
        CANCEL.store(true, Ordering::Release);
        1
    } else {
        0
    }
}
fn main() {
    unsafe {
        SetConsoleCtrlHandler(Some(interrupt), 1);
    }
    if let Err(e) = execute() {
        eprintln!("{e}");
        std::process::exit(1);
    }
}
const HELP: &str = r#"AudioStretch 0.3.0 - Rubber Band R3/Finer frontend
Legacy: audio-stretch-cli TOOLS INPUT OUTPUT TEMPO_CHANGE ENCODING [options]
Render: audio-stretch-cli --tools DIR --input AUDIO --output AUDIO [options]
Replay: audio-stretch-cli --tools DIR --job JOB_OR_RECEIPT.json [--overwrite]

Time (choose one): --tempo-change PERCENT, --tempo-speed MULTIPLIER,
  --bpm SOURCE:TARGET, --time RATIO, --duration SECONDS
Pitch (choose one): --pitch SEMITONES, --cents CENTS, --frequency RATIO
Maps: --timemap FILE, --pitchmap FILE OR --freqmap FILE, --map-seconds
Processing: --formant --centre-focus --realtime --draft --pitch-hq --clamp
Diagnostics: --debug 0..3 --diagnostic-ticks --quiet
Other: --encoding float|24|16 --overwrite --plan --receipt NEW_FILE
  --tool-help normal|full|version, --version, --help

Output: WAV (float/24/16), FLAC (24/16). Tools may be a complete flat tools
folder or the managed tools root. Time maps require offline mode; pitch/frequency
maps force real-time file processing. Diagnostic level 3 adds audible ticks.
--plan prints the normalized plan and performs no render. Ctrl+C cancels safely.
Exported jobs/receipts use the same validated Rust request model as the GUI."#;
fn number(s: &str) -> Result<f64, String> {
    s.parse()
        .map_err(|_| format!("Invalid number: {s}. Use a decimal point."))
}
fn settings(r: &mut Request) -> &mut Transform {
    r.transform.get_or_insert(Transform {
        time_value: r.tempo_change,
        ..Default::default()
    })
}
fn execute() -> Result<(), String> {
    let args: Vec<_> = std::env::args().skip(1).collect();
    if args.is_empty() || args == ["--help"] || args == ["-h"] {
        println!("{HELP}");
        return Ok(());
    }
    if args == ["--version"] {
        println!("AudioStretch {}", env!("CARGO_PKG_VERSION"));
        return Ok(());
    }
    let mut request = Request {
        operation: "start".into(),
        encoding: "float".into(),
        ..Default::default()
    };
    let mut tools = std::env::current_exe()
        .map_err(|e| e.to_string())?
        .parent()
        .unwrap()
        .join("tools")
        .to_string_lossy()
        .into_owned();
    let mut offset = 0;
    if !args[0].starts_with('-') {
        if args.len() < 5 {
            return Err(HELP.into());
        }
        tools = args[0].clone();
        request.input = args[1].clone();
        request.output = args[2].clone();
        request.tempo_change = number(&args[3])?;
        request.encoding = args[4].clone();
        offset = 5;
    }
    // Load jobs first; explicit options after --job may override their fields. Exported jobs disable overwrite.
    if let Some(index) = args.iter().position(|a| a == "--job") {
        let path = args.get(index + 1).ok_or("--job requires a file")?;
        let json: serde_json::Value =
            serde_json::from_slice(&std::fs::read(path).map_err(|e| e.to_string())?)
                .map_err(|e| e.to_string())?;
        request = serde_json::from_value(json.get("request").unwrap_or(&json).clone())
            .map_err(|e| format!("Invalid render job: {e}"))?;
        request.overwrite = false;
    }
    let mut i = offset;
    let mut time_flags = 0;
    let mut pitch_flags = 0;
    let mut maps = vec![];
    let mut map_seconds = false;
    let mut only_plan = false;
    let mut receipt_path = None;
    let mut help_mode = None;
    while i < args.len() {
        let flag = args[i].as_str();
        i += 1;
        let mut value = || -> Result<&str, String> {
            let v = args.get(i).ok_or_else(|| format!("{flag} needs a value"))?;
            i += 1;
            Ok(v)
        };
        match flag {
            "--tools" => tools = value()?.into(),
            "--input" => request.input = value()?.into(),
            "--output" => request.output = value()?.into(),
            "--encoding" => request.encoding = value()?.into(),
            "--job" => {
                value()?;
            }
            "--tempo-change" | "--tempo-speed" | "--time" | "--duration" | "--bpm" => {
                time_flags += 1;
                let v = value()?;
                let t = settings(&mut request);
                t.time_mode = match flag {
                    "--tempo-change" => TimeMode::TempoChange,
                    "--tempo-speed" => TimeMode::TempoMultiplier,
                    "--time" => TimeMode::TimeRatio,
                    "--duration" => TimeMode::Duration,
                    _ => TimeMode::Bpm,
                } as i32;
                if flag == "--bpm" {
                    let (from, to) = v.split_once(':').ok_or("BPM syntax is SOURCE:TARGET")?;
                    t.source_bpm = number(from)?;
                    t.target_bpm = number(to)?;
                } else {
                    t.time_value = number(v)?;
                }
            }
            "--pitch" | "--cents" | "--frequency" => {
                pitch_flags += 1;
                let v = number(value()?)?;
                let t = settings(&mut request);
                t.pitch_mode = match flag {
                    "--pitch" => PitchMode::Semitones,
                    "--cents" => PitchMode::Cents,
                    _ => PitchMode::Frequency,
                } as i32;
                t.pitch_value = v;
            }
            "--formant" => settings(&mut request).preserve_formants = true,
            "--centre-focus" => request.centre_focus = true,
            "--realtime" => settings(&mut request).realtime = true,
            "--draft" => settings(&mut request).short_window = true,
            "--pitch-hq" => settings(&mut request).pitch_hq = true,
            "--clamp" => settings(&mut request).clamp = true,
            "--debug" => {
                settings(&mut request).debug = value()?.parse().map_err(|_| "Debug must be 0..3")?
            }
            "--diagnostic-ticks" => settings(&mut request).diagnostic_ticks = true,
            "--quiet" => settings(&mut request).quiet = true,
            "--overwrite" => request.overwrite = true,
            "--plan" => only_plan = true,
            "--receipt" => receipt_path = Some(value()?.to_string()),
            "--tool-help" => help_mode = Some(value()?.to_string()),
            "--map-seconds" => map_seconds = true,
            "--timemap" | "--pitchmap" | "--freqmap" => {
                maps.push((
                    match flag {
                        "--timemap" => MapKind::Time,
                        "--pitchmap" => MapKind::Pitch,
                        _ => MapKind::FrequencyMap,
                    },
                    value()?.to_string(),
                ));
            }
            _ => return Err(format!("Unknown option: {flag}\n{HELP}")),
        }
    }
    if time_flags > 1 || pitch_flags > 1 {
        return Err("Choose one time mode and one pitch entry mode.".into());
    }
    if maps
        .iter()
        .filter(|(kind, _)| *kind == MapKind::Time)
        .count()
        > 1
        || maps
            .iter()
            .filter(|(kind, _)| *kind != MapKind::Time)
            .count()
            > 1
    {
        return Err(
            "Choose at most one time map and either one pitch map or one frequency map.".into(),
        );
    }
    let mut engine = Engine::new(tools);
    if let Some(mode) = help_mode {
        let operation = match mode.as_str() {
            "normal" => "help",
            "full" => "full_help",
            "version" => "tool_version",
            _ => return Err("Tool help is normal, full or version.".into()),
        };
        let reply = engine.call(Request {
            operation: operation.into(),
            ..Default::default()
        });
        if !reply.error.is_empty() {
            return Err(reply.error);
        }
        println!("{}", reply.text);
        return Ok(());
    }
    if request.input.is_empty() || request.output.is_empty() {
        return Err("Choose both input and output audio paths. See --help.".into());
    }
    request.input = std::path::absolute(&request.input)
        .map_err(|e| e.to_string())?
        .to_string_lossy()
        .into_owned();
    request.output = std::path::absolute(&request.output)
        .map_err(|e| e.to_string())?
        .to_string_lossy()
        .into_owned();
    let probe = engine.call(Request {
        operation: "probe".into(),
        input: request.input.clone(),
        ..Default::default()
    });
    if !probe.error.is_empty() {
        return Err(probe.error);
    }
    audio_stretch_core::validate_source_identity(
        request.source_info.as_ref(),
        probe.info.as_ref().unwrap(),
    )?;
    request.source_info = probe.info;
    for (kind, path) in maps {
        let mut parse = request.clone();
        parse.operation = "map_import".into();
        parse.map_kind = kind as i32;
        parse.map_seconds = map_seconds;
        parse.map_text =
            std::fs::read_to_string(&path).map_err(|e| format!("Cannot read map {path}: {e}"))?;
        let reply = engine.call(parse);
        if !reply.error.is_empty() {
            return Err(reply.error);
        }
        request.maps = reply.maps;
    }
    let mut check = request.clone();
    check.operation = "plan".into();
    let reply = engine.call(check);
    if !reply.error.is_empty() {
        return Err(reply.error);
    }
    if only_plan {
        println!(
            "{}",
            serde_json::to_string_pretty(&reply.plan.unwrap()).map_err(|e| e.to_string())?
        );
        return Ok(());
    }
    if let Some(path) = &receipt_path {
        let mut check = request.clone();
        check.operation = "validate_artifact".into();
        check.artifact_path = std::path::absolute(path)
            .map_err(|e| e.to_string())?
            .to_string_lossy()
            .into_owned();
        let reply = engine.call(check);
        if !reply.error.is_empty() {
            return Err(reply.error);
        }
        if Path::new(path).exists() {
            return Err("Receipt output already exists; choose a new receipt filename.".into());
        }
    }
    request.operation = "start".into();
    let reply = engine.call(request);
    if !reply.error.is_empty() {
        return Err(reply.error);
    }
    let mut last = String::new();
    let mut cancelled = false;
    loop {
        if CANCEL.load(Ordering::Acquire) && !cancelled {
            engine.call(Request {
                operation: "cancel".into(),
                ..Default::default()
            });
            cancelled = true;
        }
        let reply = engine.call(Request {
            operation: "poll".into(),
            ..Default::default()
        });
        let s = reply.snapshot.unwrap();
        if s.stage != last {
            eprintln!("{}: {}", s.state, s.stage);
            last = s.stage.clone();
        }
        match s.state.as_str() {
            "Completed" => {
                if let Some(path) = receipt_path {
                    use std::io::Write;
                    let mut file = std::fs::OpenOptions::new()
                        .write(true)
                        .create_new(true)
                        .open(path)
                        .map_err(|e| {
                            format!("Audio completed, but receipt could not be saved: {e}")
                        })?;
                    file.write_all(s.receipt_json.as_bytes())
                        .map_err(|e| e.to_string())?;
                }
                println!("{}", s.receipt_json);
                return Ok(());
            }
            "Failed" | "Cancelled" => return Err(format!("{}\n{}", s.error, s.log)),
            _ => std::thread::sleep(std::time::Duration::from_millis(100)),
        }
    }
}
