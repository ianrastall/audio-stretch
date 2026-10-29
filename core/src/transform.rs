use crate::{multiplier, wire::*};

pub const MAX_FRAME: u64 = i32::MAX as u64;
fn positive(value: f64, label: &str) -> Result<f64, String> {
    if !value.is_finite() || value <= 0.0 {
        return Err(format!("{label} must be finite and greater than zero."));
    }
    Ok(value)
}
pub fn coordinate(value: &str, seconds: bool, sample_rate: u32) -> Result<u64, String> {
    if seconds {
        let value = value
            .parse::<f64>()
            .map_err(|_| "A map position must be a number (use a decimal point).")?;
        if !value.is_finite() || value < 0.0 {
            return Err("Map positions cannot be negative or non-finite.".into());
        }
        let frame = (value * f64::from(sample_rate)).round();
        if frame > MAX_FRAME as f64 {
            return Err(
                "Map position exceeds the command-line utility's 32-bit frame limit.".into(),
            );
        }
        Ok(frame as u64)
    } else {
        let frame = value
            .parse::<u64>()
            .map_err(|_| "Frame positions must be nonnegative whole numbers.")?;
        if frame > MAX_FRAME {
            return Err(
                "Map position exceeds the command-line utility's 32-bit frame limit.".into(),
            );
        }
        Ok(frame)
    }
}
pub fn bind_maps(info: &AudioInfo) -> Maps {
    Maps {
        sample_rate: info.sample_rate,
        source_frames: info.frame_count,
        source_sha256: info.sha256.clone(),
        pitch_kind: MapKind::Pitch as i32,
        ..Default::default()
    }
}
pub fn validate_maps(maps: &Maps, info: &AudioInfo) -> Result<(), String> {
    if maps.time_points.is_empty() && maps.pitch_points.is_empty() {
        return Ok(());
    }
    if maps.sample_rate != info.sample_rate
        || maps.source_frames != info.frame_count
        || maps.source_sha256.is_empty()
        || maps.source_sha256 != info.sha256
    {
        return Err("These maps belong to a different source or sample rate. Reload/rebind the maps to the current source deliberately.".into());
    }
    if maps.time_points.len() > 10000 || maps.pitch_points.len() > 10000 {
        return Err("A map may contain at most 10,000 rows.".into());
    }
    let mut last = None;
    let mut target = None;
    for point in &maps.time_points {
        if point.source_frame > info.frame_count || point.target_frame > MAX_FRAME {
            return Err("A time-map point is outside the source/output frame range.".into());
        }
        if last.is_some_and(|v| point.source_frame <= v) {
            return Err(
                "Time-map source frames must increase strictly. Duplicate frames are not accepted."
                    .into(),
            );
        }
        if target.is_some_and(|v| point.target_frame <= v) {
            return Err("Time-map output frames must increase strictly.".into());
        }
        if point.source_frame == 0 && point.target_frame != 0 {
            return Err("The source's first frame must map to output frame zero.".into());
        }
        if point.source_frame > 0 && point.target_frame == 0 {
            return Err("A positive source frame must map to a positive output frame.".into());
        }
        last = Some(point.source_frame);
        target = Some(point.target_frame);
    }
    if !maps.pitch_points.is_empty()
        && !matches!(
            MapKind::try_from(maps.pitch_kind),
            Ok(MapKind::Pitch | MapKind::FrequencyMap)
        )
    {
        return Err("Choose a pitch map or a frequency map.".into());
    }
    last = None;
    for point in &maps.pitch_points {
        if point.source_frame >= info.frame_count {
            return Err("Pitch-map positions must fall before the end of the source.".into());
        }
        if last.is_some_and(|v| point.source_frame <= v) {
            return Err("Pitch/frequency-map source frames must increase strictly. Duplicate frames are not accepted.".into());
        }
        let ratio = if maps.pitch_kind == MapKind::Pitch as i32 {
            2_f64.powf(point.value / 12.0)
        } else {
            point.value
        };
        positive(ratio, "Map frequency ratio")?;
        if !point.value.is_finite() {
            return Err("Map values must be finite.".into());
        }
        last = Some(point.source_frame);
    }
    Ok(())
}
pub fn import_map(request: &Request, info: &AudioInfo) -> Result<Maps, String> {
    let kind = MapKind::try_from(request.map_kind).map_err(|_| "Unknown map kind.")?;
    let mut maps = request.maps.clone().unwrap_or_else(|| bind_maps(info));
    validate_maps(&maps, info)?;
    if kind == MapKind::Time {
        maps.time_points.clear();
    } else {
        maps.pitch_points.clear();
        maps.pitch_kind = kind as i32;
    }
    for (line_index, line) in request.map_text.lines().enumerate() {
        let values: Vec<_> = line.split_whitespace().collect();
        if values.is_empty() {
            continue;
        }
        let mut result = || -> Result<(), String> {
            if values.len() != 2 {
                return Err("Expected exactly two columns.".into());
            }
            let source = coordinate(values[0], request.map_seconds, info.sample_rate)?;
            if kind == MapKind::Time {
                maps.time_points.push(TimeAnchor {
                    source_frame: source,
                    target_frame: coordinate(values[1], request.map_seconds, info.sample_rate)?,
                });
            } else {
                maps.pitch_points.push(PitchAnchor {
                    source_frame: source,
                    value: values[1]
                        .parse()
                        .map_err(|_| "Map shift/ratio must be numeric (use a decimal point).")?,
                });
            }
            Ok(())
        };
        result().map_err(|e| format!("Map line {}: {e}", line_index + 1))?;
    }
    validate_maps(&maps, info)?;
    Ok(maps)
}
pub fn export_map(maps: &Maps, kind: MapKind, seconds: bool) -> String {
    let position = |frame: u64| {
        if seconds {
            (frame as f64 / f64::from(maps.sample_rate)).to_string()
        } else {
            frame.to_string()
        }
    };
    if kind == MapKind::Time {
        maps.time_points
            .iter()
            .map(|p| {
                format!(
                    "{} {}\n",
                    position(p.source_frame),
                    position(p.target_frame)
                )
            })
            .collect()
    } else {
        maps.pitch_points
            .iter()
            .map(|p| format!("{} {}\n", position(p.source_frame), p.value))
            .collect()
    }
}
pub fn plan(request: &Request, info: &AudioInfo) -> Result<RenderPlan, String> {
    if !request.output.is_empty() {
        let ext = std::path::Path::new(&request.output)
            .extension()
            .and_then(|s| s.to_str())
            .unwrap_or("")
            .to_ascii_lowercase();
        if !matches!(
            (ext.as_str(), request.encoding.as_str()),
            ("wav", "float" | "24" | "16") | ("flac", "24" | "16")
        ) {
            return Err(
                "Choose WAV (float/24/16) or FLAC (24/16) and a matching output extension.".into(),
            );
        }
    }
    if info.sample_rate == 0
        || info.frame_count == 0
        || info.frame_count > MAX_FRAME
        || !info.duration.is_finite()
        || info.duration <= 0.0
    {
        return Err("Audio must have a known positive duration and at most 2,147,483,647 source frames (the upstream CLI uses 32-bit frame counters).".into());
    }
    let legacy = request.transform.is_none();
    let transform = request.transform.unwrap_or(Transform {
        time_value: request.tempo_change,
        ..Default::default()
    });
    let time_mode = TimeMode::try_from(transform.time_mode).map_err(|_| "Unknown time mode.")?;
    let value = transform.time_value;
    let ratio = match time_mode {
        TimeMode::TempoChange => {
            1.0 / if legacy {
                multiplier(value)?
            } else {
                positive(1.0 + value / 100.0, "Tempo multiplier")?
            }
        }
        TimeMode::TempoMultiplier => 1.0 / positive(value, "Tempo multiplier")?,
        TimeMode::Bpm => {
            positive(transform.source_bpm, "Source BPM")?
                / positive(transform.target_bpm, "Target BPM")?
        }
        TimeMode::TimeRatio => positive(value, "Duration multiplier")?,
        TimeMode::Duration => positive(value, "Target duration")? / info.duration,
    };
    positive(ratio, "Time ratio")?;
    let expected = info.duration * ratio;
    let output_frames = (expected * f64::from(info.sample_rate)).round();
    if !output_frames.is_finite() || !(1.0..=MAX_FRAME as f64).contains(&output_frames) {
        return Err("The requested output must contain 1–2,147,483,647 frames.".into());
    }
    let pitch_mode =
        PitchMode::try_from(transform.pitch_mode).map_err(|_| "Unknown pitch mode.")?;
    let frequency = match pitch_mode {
        PitchMode::Unchanged => 1.0,
        PitchMode::Semitones => 2_f64.powf(transform.pitch_value / 12.0),
        PitchMode::Cents => 2_f64.powf(transform.pitch_value / 1200.0),
        PitchMode::Frequency => positive(transform.pitch_value, "Frequency ratio")?,
    };
    positive(frequency, "Effective frequency ratio")?;
    if pitch_mode != PitchMode::Unchanged && !transform.pitch_value.is_finite() {
        return Err("Pitch must be finite.".into());
    }
    if transform.debug > 3 || (transform.debug == 3 && !transform.diagnostic_ticks) {
        return Err(
            "Debug level 3 adds audible ticks; explicitly enable Diagnostic render to use it."
                .into(),
        );
    }
    let maps = request.maps.clone().unwrap_or_default();
    validate_maps(&maps, info)?;
    let realtime = transform.realtime || !maps.pitch_points.is_empty();
    if realtime && !maps.time_points.is_empty() {
        return Err("R3 time maps require offline mode. Pitch/frequency maps force real-time mode, so they cannot be combined with a time map.".into());
    }
    if transform.preserve_formants && frequency == 1.0 && maps.pitch_points.is_empty() {
        return Err(
            "Formant preservation requires a pitch change or a pitch/frequency map.".into(),
        );
    }
    if maps.pitch_kind == MapKind::Pitch as i32 {
        for point in &maps.pitch_points {
            positive(
                frequency * 2_f64.powf(point.value / 12.0),
                "Combined map frequency ratio",
            )?;
        }
    } else {
        for point in &maps.pitch_points {
            positive(frequency * point.value, "Combined map frequency ratio")?;
        }
    }
    let mut warnings = vec![];
    let mut options = vec!["--fine".into()];
    match time_mode {
        TimeMode::TempoChange => options.extend(["--tempo".into(), (1.0 / ratio).to_string()]),
        TimeMode::TempoMultiplier => options.extend(["--tempo".into(), value.to_string()]),
        TimeMode::Bpm => options.extend([
            "--tempo".into(),
            format!("{}:{}", transform.source_bpm, transform.target_bpm),
        ]),
        TimeMode::TimeRatio => options.extend(["--time".into(), value.to_string()]),
        TimeMode::Duration => options.extend(["--duration".into(), value.to_string()]),
    }
    if pitch_mode == PitchMode::Semitones {
        options.extend(["--pitch".into(), transform.pitch_value.to_string()]);
    } else if pitch_mode == PitchMode::Cents {
        options.extend([
            "--pitch".into(),
            (transform.pitch_value / 100.0).to_string(),
        ]);
    } else if pitch_mode == PitchMode::Frequency {
        options.extend(["--frequency".into(), transform.pitch_value.to_string()]);
    }
    if transform.preserve_formants {
        options.push("--formant".into());
    }
    if request.centre_focus {
        if info.channels == 2 {
            options.push("--centre-focus".into());
        } else {
            warnings
                .push("Centre focus applies only to stereo; it is omitted for this source.".into());
        }
    }
    if realtime {
        options.push("--realtime".into());
        warnings
            .push("Real-time engine file render: exact output duration is not guaranteed.".into());
    }
    if !maps.pitch_points.is_empty() {
        warnings.push("The pitch/frequency map selects high-consistency pitch processing; offsets are relative to the initial pitch.".into());
    }
    if transform.short_window {
        options.push("--window-short".into());
        warnings.push("Short-window draft mode trades quality for lower CPU use.".into());
    }
    if transform.pitch_hq {
        if !realtime {
            return Err("Real-time pitch quality has no effect offline. Disable it or select real-time processing.".into());
        }
        if maps.pitch_points.is_empty() {
            options.push("--pitch-hq".into());
        } else {
            warnings.push(
                "The map overrides real-time high-quality pitch with high-consistency processing."
                    .into(),
            );
        }
    }
    if transform.clamp {
        options.push("--ignore-clipping".into());
        warnings.push(
            "Clipping is clamped without a recovery restart and may distort the result.".into(),
        );
    }
    if transform.debug > 0 {
        options.extend(["--debug".into(), transform.debug.to_string()]);
    }
    if transform.debug == 3 {
        warnings.push("Diagnostic render: audible debug ticks are present in the output.".into());
    }
    if transform.quiet {
        options.push("--quiet".into());
        if !transform.clamp {
            warnings.push("Quiet mode suppresses some clipping recovery/gain diagnostics.".into());
        }
    }
    let mut normalized = maps.clone();
    if !normalized.time_points.is_empty() {
        let last = normalized.time_points.last().unwrap();
        if last.source_frame == info.frame_count && last.target_frame != output_frames as u64 {
            return Err("The final time-map target must match the duration selected in Time. Change the aggregate duration or the final point.".into());
        }
        if last.source_frame < info.frame_count && last.target_frame >= output_frames as u64 {
            return Err(
                "An intermediate time-map target must fall before the selected output duration."
                    .into(),
            );
        }
        if normalized.time_points.last().unwrap().source_frame != info.frame_count {
            normalized.time_points.push(TimeAnchor {
                source_frame: info.frame_count,
                target_frame: output_frames as u64,
            });
        }
        // R3 4.0.0 divides the first target by the first source in updateRatioFromMap.
        // The origin is implicit; passing an explicit 0 -> 0 makes its ratio NaN.
        if normalized.time_points[0].source_frame == 0 {
            normalized.time_points.remove(0);
            warnings.push("The origin 0 → 0 is implicit and omitted from the native time map for R3 compatibility.".into());
        }
        validate_maps(&normalized, info)?;
    }
    let summary = format!(
        "Time {:.9}× · Frequency {:.9}× · Expected {:.6}s · R3/Finer {}",
        ratio,
        frequency,
        expected,
        if realtime {
            "real-time file render"
        } else {
            "offline"
        }
    );
    Ok(RenderPlan {
        time_ratio: ratio,
        frequency_ratio: frequency,
        expected_duration: expected,
        realtime,
        options,
        time_map_text: export_map(&normalized, MapKind::Time, false),
        pitch_map_text: export_map(&maps, MapKind::Pitch, false),
        pitch_kind: maps.pitch_kind,
        warnings,
        summary,
    })
}

pub fn convert(request: &Request, info: &AudioInfo, time: bool) -> Result<Transform, String> {
    let plan = plan(request, info)?;
    let mut result = request.transform.unwrap_or(Transform {
        time_value: request.tempo_change,
        ..Default::default()
    });
    if time {
        let mode =
            TimeMode::try_from(request.target_time_mode).map_err(|_| "Unknown time mode.")?;
        result.time_value = match mode {
            TimeMode::TempoChange => (1.0 / plan.time_ratio - 1.0) * 100.0,
            TimeMode::TempoMultiplier => 1.0 / plan.time_ratio,
            TimeMode::TimeRatio => plan.time_ratio,
            TimeMode::Duration => plan.expected_duration,
            TimeMode::Bpm => 0.0,
        };
        if mode == TimeMode::Bpm {
            if !result.source_bpm.is_finite() || result.source_bpm <= 0.0 {
                result.source_bpm = 120.0;
            }
            result.target_bpm = result.source_bpm / plan.time_ratio;
        }
        result.time_mode = mode as i32;
    } else {
        let mode =
            PitchMode::try_from(request.target_pitch_mode).map_err(|_| "Unknown pitch mode.")?;
        result.pitch_value = match mode {
            PitchMode::Unchanged => 0.0,
            PitchMode::Semitones => plan.frequency_ratio.log2() * 12.0,
            PitchMode::Cents => plan.frequency_ratio.log2() * 1200.0,
            PitchMode::Frequency => plan.frequency_ratio,
        };
        result.pitch_mode = mode as i32;
        if mode == PitchMode::Unchanged
            && request
                .maps
                .as_ref()
                .is_none_or(|m| m.pitch_points.is_empty())
        {
            result.preserve_formants = false;
        }
    }
    Ok(result)
}

#[cfg(test)]
mod tests {
    use super::*;
    fn info() -> AudioInfo {
        AudioInfo {
            sample_rate: 48000,
            channels: 2,
            duration: 6.0,
            frame_count: 288000,
            sha256: "source".into(),
            ..Default::default()
        }
    }
    fn request() -> Request {
        Request {
            transform: Some(Transform::default()),
            ..Default::default()
        }
    }
    #[test]
    fn constant_modes_and_pitch() {
        let mut r = request();
        for (mode, value, expected) in [
            (TimeMode::TempoChange, 25.0, 4.8),
            (TimeMode::TempoMultiplier, 1.25, 4.8),
            (TimeMode::TimeRatio, 2.0, 12.0),
            (TimeMode::Duration, 3.0, 3.0),
        ] {
            let t = r.transform.as_mut().unwrap();
            t.time_mode = mode as i32;
            t.time_value = value;
            assert!((plan(&r, &info()).unwrap().expected_duration - expected).abs() < 1e-9);
        }
        let t = r.transform.as_mut().unwrap();
        t.time_mode = TimeMode::Bpm as i32;
        t.source_bpm = 120.0;
        t.target_bpm = 150.0;
        t.pitch_mode = PitchMode::Cents as i32;
        t.pitch_value = 1200.0;
        let p = plan(&r, &info()).unwrap();
        assert_eq!(p.frequency_ratio, 2.0);
        assert!((p.expected_duration - 4.8).abs() < 1e-9);
    }
    #[test]
    fn unsafe_values_and_diagnostics_rejected() {
        let mut r = request();
        for v in [-100.0, f64::NAN, f64::INFINITY] {
            r.transform.as_mut().unwrap().time_value = v;
            assert!(plan(&r, &info()).is_err());
        }
        r.transform = Some(Transform {
            debug: 3,
            ..Default::default()
        });
        assert!(plan(&r, &info()).is_err());
        r.transform.as_mut().unwrap().diagnostic_ticks = true;
        assert!(
            plan(&r, &info())
                .unwrap()
                .warnings
                .iter()
                .any(|w| w.contains("ticks"))
        );
    }
    #[test]
    fn map_import_rounds_seconds_and_rejects_duplicates() {
        let mut r = request();
        r.map_seconds = true;
        r.map_text = "0.5 0.75\n2 2.5".into();
        let m = import_map(&r, &info()).unwrap();
        assert_eq!(m.time_points[0].source_frame, 24000);
        assert_eq!(m.time_points[0].target_frame, 36000);
        r.map_text = "1 1\n1 2".into();
        assert!(import_map(&r, &info()).is_err());
        assert!(coordinate("2147483648", false, 48000).is_err());
    }
    #[test]
    fn maps_are_source_bound_and_modes_are_enforced() {
        let mut r = request();
        let mut m = bind_maps(&info());
        m.time_points.push(TimeAnchor {
            source_frame: 96000,
            target_frame: 48000,
        });
        r.maps = Some(m);
        assert_eq!(
            plan(&r, &info()).unwrap().time_map_text,
            "96000 48000\n288000 288000\n"
        );
        r.maps.as_mut().unwrap().time_points.insert(
            0,
            TimeAnchor {
                source_frame: 0,
                target_frame: 0,
            },
        );
        assert_eq!(
            plan(&r, &info()).unwrap().time_map_text,
            "96000 48000\n288000 288000\n"
        );
        r.transform.as_mut().unwrap().realtime = true;
        assert!(plan(&r, &info()).is_err());
        r.transform.as_mut().unwrap().realtime = false;
        r.maps.as_mut().unwrap().pitch_points.push(PitchAnchor {
            source_frame: 0,
            value: 12.0,
        });
        assert!(plan(&r, &info()).is_err());
        r.maps.as_mut().unwrap().time_points.clear();
        assert!(plan(&r, &info()).unwrap().realtime);
        r.maps.as_mut().unwrap().source_sha256 = "changed".into();
        assert!(plan(&r, &info()).is_err());
    }
}
