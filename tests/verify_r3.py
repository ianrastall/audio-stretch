"""Measured full-R3 acceptance using only Python's standard library and the real tools."""
import array
import cmath
import hashlib
import json
import math
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import wave

publish, tools = (Path(p).resolve() for p in sys.argv[1:3])
cli = publish / 'audio-stretch-cli.exe'
active = tools / 'ffmpeg' / 'active.txt'
ffmpeg = tools / 'ffmpeg' / 'versions' / active.read_text().strip() / 'ffmpeg.exe' if active.exists() else tools / 'ffmpeg.exe'
rate = 44100
results = []

def run(args, success=True):
    p = subprocess.run([str(a) for a in args], capture_output=True, timeout=120)
    assert (p.returncode == 0) == success, p.stderr.decode(errors='replace')[-6000:]
    return p.stdout

def samples(path):
    return array.array('f', run([ffmpeg, '-v', 'error', '-i', path, '-ac', '1', '-f', 'f32le', '-']))

def hz(data, start=.7, end=2.5):
    data = data[int(start*rate):int(end*rate)]
    crossings = [i-1-data[i-1]/(data[i]-data[i-1]) for i in range(1,len(data)) if data[i-1] <= 0 < data[i]]
    return (len(crossings)-1)*rate/(crossings[-1]-crossings[0])

def write(path, data):
    with wave.open(str(path), 'wb') as wav:
        wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(rate)
        wav.writeframes(b''.join(struct.pack('<h', max(-32767,min(32767,int(x*32767)))) for x in data))

with tempfile.TemporaryDirectory(prefix='audiostretch-r3-') as directory:
    root = Path(directory)
    source = root/'tone.wav'
    write(source, (.3*math.sin(2*math.pi*440*i/rate) for i in range(rate*6)))
    def render(name, flags, input=source):
        output = root/(name+'.wav')
        receipt = json.loads(run([cli, '--tools', tools, '--input', input, '--output', output, *flags]))
        assert receipt['source']['sha256'] == hashlib.sha256(input.read_bytes()).hexdigest()
        assert receipt['output']['sha256'] == hashlib.sha256(output.read_bytes()).hexdigest()
        assert receipt['plan']['options'][0] == '--fine'
        assert receipt['tools']
        return output, receipt

    for name, flags, duration, pitch in [
        ('up12',['--pitch','12'],6,880), ('down12',['--pitch','-12'],6,220),
        ('cents',['--cents','700'],6,440*2**(7/12)), ('frequency',['--frequency','1.5'],6,660),
        ('bpm',['--bpm','120:150'],4.8,440), ('ratio',['--time','1.25'],7.5,440),
        ('target',['--duration','4.2'],4.2,440), ('speed',['--tempo-speed','1.25'],4.8,440),
        ('combined',['--duration','5','--pitch','12'],5,880),
        ('realtime-hq',['--realtime','--pitch-hq','--pitch','3'],6,440*2**.25),
        ('draft',['--draft','--tempo-change','20','--quiet','--debug','1'],5,440),
    ]:
        output, receipt = render(name, flags)
        data = samples(output); measured = hz(data)
        assert abs(measured-pitch)<2, (name, measured, pitch)
        assert abs(len(data)/rate-duration)<(.3 if receipt['plan']['realtime'] else .03), (name,len(data)/rate,duration)
        results.append(dict(case=name,duration=len(data)/rate,frequency_hz=measured))

    for kind, text in [('pitchmap','0 0\n88200 12\n176400 -12\n'),('freqmap','0 1\n88200 2\n176400 0.5\n')]:
        mapfile = root/(kind+'.txt'); mapfile.write_text(text)
        initial = ['--pitch','3'] if kind == 'freqmap' else []
        output, receipt = render(kind, ['--'+kind,mapfile,*initial])
        data = samples(output)
        frequencies = [hz(data,a,b) for a,b in [(0.7,1.5),(2.8,3.6),(4.7,5.5)]]
        for actual, expected in zip(frequencies,[440,880,220]): assert abs(actual-expected*(2**.25 if initial else 1))<2,(kind,frequencies)
        assert receipt['plan']['realtime'] and receipt['plan']['pitch_map_text']
        results.append(dict(case=kind,frequencies_hz=frequencies,duration=len(data)/rate))

    impulses = root/'impulses.wav'
    # Short percussive bursts at known anchors, on silence.
    write(impulses, (.7*math.exp(-((i%rate)/80))*math.sin(2*math.pi*2000*i/rate) if i//rate in (1,3,5) else 0 for i in range(rate*6)))
    mapfile = root/'time.txt'; mapfile.write_text('1 0.75\n3 3.25\n5 5.5\n')
    output, receipt = render('timemap',['--timemap',mapfile,'--map-seconds'], impulses)
    data = samples(output); peaks=[]
    import shutil
    shutil.copyfile(output, publish.parent/'time-map-diagnostic.wav')
    (publish.parent/'time-map-diagnostic.json').write_text(json.dumps(receipt,indent=2))
    for expected in [.75,3.25,5.5]:
        a,b=int((expected-.15)*rate),int((expected+.15)*rate)
        peak=max(range(a,b),key=lambda i:abs(data[i]))/rate
        assert abs(peak-expected)<.055,(expected,peak)
        peaks.append(peak)
    assert not receipt['plan']['time_map_text'].startswith('0 0\n')
    results.append(dict(case='time-map-impulses',peaks_seconds=peaks))

    # Synthesized voiced vowel: harmonic source shaped by two fixed resonant bands.
    vowel=root/'voiced-vowel.wav'
    harmonics=[(f,math.exp(-((f-700)/130)**2)+.65*math.exp(-((f-1300)/180)**2)) for f in range(100,3001,100)]
    write(vowel,(sum(amplitude*math.sin(2*math.pi*f*i/rate) for f,amplitude in harmonics)*.08 for i in range(rate*3)))
    plain,_=render('vowel-shift',['--pitch','12'],vowel)
    preserved,_=render('vowel-formants',['--pitch','12','--formant'],vowel)
    for path in [plain,preserved,vowel]: shutil.copyfile(path,publish.parent/path.name)
    def band(data, frequencies):
        data=data[rate:rate+8192]
        total=0
        for f in frequencies:
            step=cmath.exp(-2j*math.pi*f/rate); phase=1+0j; value=0j
            for i,x in enumerate(data):
                value+=x*(.5-.5*math.cos(2*math.pi*i/(len(data)-1)))*phase; phase*=step
            total+=abs(value)**2
        return total
    def envelope_ratio(path):
        data=samples(path)
        return band(data,[600,800,1200])/band(data,[2400,2600,2800])
    plain_ratio,formant_ratio=envelope_ratio(plain),envelope_ratio(preserved)
    assert formant_ratio > plain_ratio*2,(plain_ratio,formant_ratio)
    results.append(dict(case='voiced-formant-envelope',unpreserved_ratio=plain_ratio,preserved_ratio=formant_ratio))

    # Loud source exercises actual restart/clamp logging and bounded final audio.
    loud=root/'loud.wav'
    run([ffmpeg,'-v','error','-i',source,'-af','volume=4','-c:a','pcm_f32le',loud])
    for name,flags in [('recover',[]),('clamp',['--clamp'])]:
        output,receipt=render(name,['--pitch','7',*flags],loud)
        assert max(abs(x) for x in samples(output))<=1.00001
        if name == 'recover': assert receipt['clipping_messages'], receipt['rubberband_log']
        else: assert '--ignore-clipping' in receipt['plan']['options'] and not receipt['clipping_messages']
        results.append(dict(case=name,clipping_messages=receipt['clipping_messages']))

    output,receipt=render('ticks',['--debug','3','--diagnostic-ticks'])
    assert any('ticks' in warning for warning in receipt['plan']['warnings'])
    results.append(dict(case='explicit-debug-ticks',passed=True))

    # Receipt replay carries embedded map snapshots; original map edits cannot change it.
    mapfile=root/'replay-map.txt';mapfile.write_text('0 0\n88200 12\n')
    _,receipt=render('replay-original',['--pitchmap',mapfile])
    saved=root/'receipt.json'; saved.write_text(json.dumps(receipt))
    mapfile.write_text('0 -12\n')
    replay=root/'replayed.wav'
    replay_receipt=json.loads(run([cli,'--tools',tools,'--job',saved,'--output',replay]))
    assert replay_receipt['plan']['pitch_map_text']==receipt['plan']['pitch_map_text']
    assert abs(hz(samples(replay),2.8,3.6)-880)<2
    results.append(dict(case='receipt-replay-immutable-maps',passed=True))
    changed=root/'changed.wav'; changed.write_bytes(source.read_bytes()+b'changed source')
    run([cli,'--tools',tools,'--job',saved,'--input',changed,'--output',root/'changed-result.wav'],False)
    assert not (root/'changed-result.wav').exists()
    results.append(dict(case='replay-rejects-changed-source',passed=True))

    for flags in [['--debug','3'],['--duration','0'],['--frequency','0'],['--pitch-hq'],['--tempo-change','nan']]:
        run([cli,'--tools',tools,'--input',source,'--output',root/'invalid.wav',*flags],False)
    for text in ['0 0\n0 1\n','2 1\n1 2\n','999999999999 1\n','0 nan\n','0 0 extra\n']:
        mapfile=root/'invalid-map.txt';mapfile.write_text(text)
        run([cli,'--tools',tools,'--input',source,'--output',root/'invalid.wav','--pitchmap',mapfile],False)
    mapfile.write_text('0 0\n44100 44100\n')
    run([cli,'--tools',tools,'--input',source,'--output',root/'invalid.wav','--timemap',mapfile,'--realtime'],False)
    assert not (root/'invalid.wav').exists()
    results.append(dict(case='invalid-settings-and-maps-preserve-output',passed=True))

    for mode in ['normal','full','version']:
        assert run([cli,'--tools',tools,'--tool-help',mode]).strip()
    results.append(dict(case='installed-help-exit-status',passed=True))

report=publish.parent/'r3-verification.json'
report.write_text(json.dumps(results,indent=2),encoding='utf-8')
print(json.dumps(results,indent=2))
