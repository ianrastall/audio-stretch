"""End-to-end pitch/duration/format checks against real bundled tools. No third-party Python dependencies."""
import array
import hashlib
import json
import math
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import wave

publish = Path(sys.argv[1]).resolve()
ffmpeg = publish / "tools/ffmpeg.exe"
ffprobe = publish / "tools/ffprobe.exe"
cli = publish / "audio-stretch-cli.exe"


def run(args):
    p = subprocess.run([str(a) for a in args], capture_output=True, timeout=120)
    if p.returncode:
        raise AssertionError(p.stderr.decode(errors="replace")[-3000:])
    return p.stdout


def probe(path):
    return json.loads(run([ffprobe, "-v", "error", "-select_streams", "a:0", "-show_streams", "-show_format", "-of", "json", path]))


def frequency(path, rate):
    raw = run([ffmpeg, "-v", "error", "-i", path, "-map", "0:a:0", "-ac", "1", "-f", "f32le", "-"])
    samples = array.array("f", raw)
    samples = samples[rate // 2: -rate // 2]
    crossings = []
    for i in range(1, len(samples)):
        if samples[i - 1] <= 0 < samples[i]:
            crossings.append(i - 1 + (-samples[i - 1]) / (samples[i] - samples[i - 1]))
    return (len(crossings) - 1) * rate / (crossings[-1] - crossings[0])


results = []
with tempfile.TemporaryDirectory(prefix="audio-stretch-verify-") as temp:
    root = Path(temp)
    source = root / "Source é 音 with spaces.wav"
    rate = 44100
    frames = bytearray()
    for i in range(rate * 6):
        frames.extend(struct.pack("<h", int(12000 * math.sin(2 * math.pi * 440 * i / rate))))
    with wave.open(str(source), "wb") as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(rate)
        wav.writeframes(frames)
    original = hashlib.sha256(source.read_bytes()).hexdigest()
    for change, ext, precision in [(25, "wav", "float"), (-20, "flac", "24"), (5.125, "wav", "16")]:
        output = root / f"Result 音 {change}.{ext}"
        run([cli, publish / "tools", source, output, change, precision])
        data = probe(output)
        stream = data["streams"][0]
        actual = float(data["format"]["duration"])
        expected = 6 / (1 + change / 100)
        measured = frequency(output, rate)
        assert abs(actual - expected) < 0.03, (actual, expected)
        assert abs(measured - 440) < 0.5, measured
        assert int(stream["sample_rate"]) == rate and stream["channels"] == 1
        results.append({"tempo_change": change, "encoding": precision, "format": ext, "duration": actual, "expected": expected, "frequency_hz": measured})

    hires = root / "stereo96.flac"
    run([ffmpeg, "-v", "error", "-i", source, "-ar", "96000", "-ac", "2", "-c:a", "flac", "-sample_fmt", "s32", "-bits_per_raw_sample", "24", hires])
    output = root / "stereo96-result.flac"
    run([cli, publish / "tools", hires, output, "10", "24", "--centre-focus"])
    info = probe(output)["streams"][0]
    assert int(info["sample_rate"]) == 96000 and info["channels"] == 2 and int(info["bits_per_raw_sample"]) == 24, info
    results.append({"case": "stereo96", "sample_rate": 96000, "channels": 2, "bit_depth": 24})

    lossy = root / "lossy.mp3"
    run([ffmpeg, "-v", "error", "-i", source, "-c:a", "libmp3lame", "-q:a", "2", lossy])
    run([cli, publish / "tools", lossy, root / "from-mp3.wav", "-10", "24"])
    assert hashlib.sha256(source.read_bytes()).hexdigest() == original
    results.append({"case": "lossy-input-to-lossless", "passed": True})

report = publish.parent / "audio-verification.json"
report.write_text(json.dumps(results, indent=2), encoding="utf-8")
print(json.dumps(results, indent=2))
