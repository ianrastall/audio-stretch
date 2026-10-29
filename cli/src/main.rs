use audio_stretch_core::{Engine, wire::Request};
fn main() {
    if let Err(e) = execute() {
        eprintln!("{e}");
        std::process::exit(1);
    }
}
fn execute() -> Result<(), String> {
    let args: Vec<_> = std::env::args().skip(1).collect();
    if args.len() < 5 {
        return Err("Usage: audio-stretch-cli TOOLS INPUT OUTPUT TEMPO_CHANGE ENCODING [--overwrite] [--centre-focus]\nENCODING: float, 24 or 16. Output extension: .wav or .flac".into());
    }
    let mut engine = Engine::new(&args[0]);
    let reply = engine.call(Request {
        operation: "start".into(),
        input: args[1].clone(),
        output: args[2].clone(),
        tempo_change: args[3].parse().map_err(|_| "Invalid tempo change")?,
        encoding: args[4].clone(),
        overwrite: args.iter().any(|s| s == "--overwrite"),
        centre_focus: args.iter().any(|s| s == "--centre-focus"),
        ..Default::default()
    });
    if !reply.error.is_empty() {
        return Err(reply.error);
    }
    let mut last = String::new();
    loop {
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
                println!("{}", s.log);
                return Ok(());
            }
            "Failed" | "Cancelled" => return Err(format!("{}\n{}", s.error, s.log)),
            _ => std::thread::sleep(std::time::Duration::from_millis(100)),
        }
    }
}
