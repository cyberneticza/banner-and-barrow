"""
Builds the game's sound assets into src/BannerAndBarrow.Game/Content/Audio and registers them in Content.mgcb.

    python tools/audio/build_audio.py --kenney <folder with unzipped Kenney packs>

Sources:
  - Kenney "RPG Audio" and "Impact Sounds" (CC0): chopping, mining, hammering, clashes, hits, footsteps, cloth, coins.
  - Synthesized here (numpy): bow twangs, torch whooshes, war horns, marching drums, fire crackle, crowd war cries
    and cheers. Deterministic (fixed random seeds).
  - Voice lines: rendered with the Windows speech synthesizer (tools/audio/speak.ps1) and roughened into shouts
    (pitch down, grit, several voices layered for group shouts, a little room). These are placeholders: drop real
    recordings with the same names into Content/Audio/Voice to replace them.

Requires numpy and scipy; voice rendering requires Windows.
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import wave

import numpy as np
from scipy import signal

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CONTENT = os.path.join(ROOT, "src", "BannerAndBarrow.Game", "Content")
AUDIO = os.path.join(CONTENT, "Audio")
SR = 22050

# ------------------------------------------------------------------ Kenney picks: key -> source files (by glob stem)
KENNEY = {
    "chop": ["rpg:chop", "impact:impactWood_medium_000", "impact:impactWood_medium_001", "impact:impactWood_medium_002"],
    "mine": [f"impact:impactMining_00{i}" for i in range(5)],
    "hammer": [f"impact:impactPlank_medium_00{i}" for i in range(5)],
    "anvil": [f"impact:impactMetal_heavy_00{i}" for i in range(5)],
    "clash": [f"impact:impactMetal_light_00{i}" for i in range(5)] + [f"impact:impactMetal_medium_00{i}" for i in range(5)],
    "armour_hit": [f"impact:impactPlate_medium_00{i}" for i in range(5)],
    "spear_hit": [f"impact:impactPunch_medium_00{i}" for i in range(5)],
    "arrow_hit": [f"impact:impactWood_light_00{i}" for i in range(5)] + [f"impact:impactSoft_medium_00{i}" for i in range(3)],
    "step": [f"impact:footstep_grass_00{i}" for i in range(5)],
    "scythe": ["rpg:knifeSlice", "rpg:knifeSlice2"],
    "sow": ["rpg:cloth1", "rpg:cloth2", "rpg:cloth3", "rpg:cloth4"],
    "coins": ["rpg:handleCoins", "rpg:handleCoins2"],
    "place": ["rpg:dropLeather", "impact:impactWood_heavy_000"],
    "complete": ["rpg:doorClose_1", "rpg:doorClose_2", "rpg:doorClose_3"],
    "recruit": ["rpg:beltHandle1", "rpg:beltHandle2", "rpg:metalLatch"],
    "collapse": [f"impact:impactWood_heavy_00{i}" for i in range(1, 5)],
    "click": ["rpg:metalClick"],
}

# ------------------------------------------------------------------ voice lines: key -> lines (voice, text)
VOICES = ["Microsoft George", "Microsoft David", "Microsoft Mark"]
LINES = {
    # Group shouts when your Regiments charge into the enemy.
    "voice_charge": ["Charge!", "For the Keep!", "At them, lads!", "Forward!", "For glory!", "Into them!"],
    # Acknowledging an attack order.
    "voice_attack": ["To battle!", "As you command!", "Onward!", "We march!", "Aye, my lord!", "Right away!"],
    # Encouragement while a fight goes on.
    "voice_rally": ["Hold the line!", "Stand firm!", "Shields up, lads!", "Don't give an inch!", "Push them back!", "They're breaking!", "Keep your heads!"],
    # Archers opening fire.
    "voice_loose": ["Loose!", "Nock, draw, loose!", "Fire at will!", "Let them have it!"],
    # An enemy Regiment wiped out.
    "voice_victory": ["Huzzah!", "Victory!", "Ha! They run!", "Is that all you've got?"],
    # A new Regiment ready.
    "voice_ready": ["Ready for orders!", "Reporting for duty!", "We stand ready!"],
}
GROUP_SHOUTS = {"voice_charge", "voice_victory", "voice_rally"}


# ------------------------------------------------------------------ wav helpers
def write_wav(path, data):
    data = np.asarray(data, dtype=np.float64)
    peak = np.max(np.abs(data)) if data.size else 0
    if peak > 0:
        data = data / peak * 0.89
    pcm = (data * 32767).astype(np.int16)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def read_wav(path):
    with wave.open(path, "rb") as w:
        rate = w.getframerate()
        channels = w.getnchannels()
        frames = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float64) / 32768
    if channels > 1:
        frames = frames.reshape(-1, channels).mean(axis=1)
    if rate != SR:
        frames = signal.resample(frames, int(len(frames) * SR / rate))
    return frames


def env(n, attack, release, curve=3.0):
    t = np.arange(n) / SR
    a = np.minimum(1, t / max(attack, 1e-4))
    r = np.clip((n / SR - t) / max(release, 1e-4), 0, 1) ** curve
    return a * r


def lowpass(x, cutoff, order=2):
    b, a = signal.butter(order, cutoff / (SR / 2), "low")
    return signal.lfilter(b, a, x)


def bandpass(x, lo, hi, order=2):
    b, a = signal.butter(order, [lo / (SR / 2), hi / (SR / 2)], "band")
    return signal.lfilter(b, a, x)


def room(x, amount=0.25):
    out = np.copy(x)
    for delay_ms, gain in [(23, 0.5), (41, 0.35), (67, 0.25), (97, 0.15)]:
        d = int(SR * delay_ms / 1000)
        out[d:] += x[:-d] * gain * amount
    return out


# ------------------------------------------------------------------ synthesis
def bow_twang(rng, pitch):
    # Karplus-Strong plucked string plus the whoosh of the arrow leaving.
    n = int(SR * 0.45)
    period = int(SR / pitch)
    buf = rng.uniform(-1, 1, period)
    out = np.zeros(n)
    for i in range(n):
        v = buf[i % period]
        out[i] = v
        buf[i % period] = 0.996 * 0.5 * (v + buf[(i + 1) % period])
    whoosh = bandpass(rng.normal(0, 1, n), 1500, 5000) * env(n, 0.005, 0.2) * 0.35
    return lowpass(out, 3000) * env(n, 0.001, 0.4) + whoosh


def whoosh(rng):
    n = int(SR * 0.5)
    t = np.arange(n) / SR
    noise = rng.normal(0, 1, n)
    center = 400 + 2500 * np.sin(np.pi * t / 0.5)
    out = np.zeros(n)
    for start in range(0, n, 512):
        seg = slice(start, min(n, start + 512))
        c = float(center[start])
        out[seg] = bandpass(noise[seg], max(80, c * 0.6), min(SR / 2 - 100, c * 1.4))
    return out * env(n, 0.12, 0.3, 2.0)


def horn(rng, base, seconds, rough):
    n = int(SR * seconds)
    t = np.arange(n) / SR
    vibrato = 1 + 0.006 * np.sin(2 * np.pi * 5.2 * t) * np.minimum(1, t / 0.4)
    glide = 1 - 0.06 * np.exp(-t * 12)
    phase = 2 * np.pi * np.cumsum(base * vibrato * glide) / SR
    tone = sum(np.sin(k * phase) / k ** (1.1 if not rough else 0.8) for k in range(1, 14))
    tone = np.tanh(tone * (1.4 if rough else 1.0))
    breath = bandpass(rng.normal(0, 1, n), 800, 3000) * 0.04
    return room(lowpass(tone, 2200 if rough else 3000) * env(n, 0.08, 0.5, 2.0) + breath, 0.5)


def drum_hit(rng, n, pitch, strength):
    t = np.arange(n) / SR
    freq = pitch * (1 + 0.8 * np.exp(-t * 30))
    body = np.sin(2 * np.pi * np.cumsum(freq) / SR) * np.exp(-t * 7)
    skin = lowpass(rng.normal(0, 1, n), 1200) * np.exp(-t * 40) * 0.5
    return (body + skin) * strength


def marching_drums(rng):
    # Two-bar loop of big war drums: BOOM . boom . BOOM boom boom .
    beat = int(SR * 0.42)
    pattern = [1.0, 0, 0.55, 0, 1.0, 0.5, 0.6, 0]
    n = beat * len(pattern)
    out = np.zeros(n)
    for i, s in enumerate(pattern):
        if s == 0:
            continue
        hit = drum_hit(rng, int(SR * 0.9), 58 + rng.uniform(-3, 3), s)
        start = i * beat
        end = min(n, start + len(hit))
        out[start:end] += hit[: end - start]
        # Let the tail wrap so the loop is seamless.
        if start + len(hit) > n:
            rest = hit[end - start:]
            out[: len(rest)] += rest
    return room(out, 0.4)


def fire_loop(rng):
    n = int(SR * 4)
    rumble = lowpass(np.cumsum(rng.normal(0, 1, n)), 300)
    rumble = rumble - lowpass(rumble, 20)
    rumble /= np.max(np.abs(rumble))
    hiss = bandpass(rng.normal(0, 1, n), 2000, 7000) * 0.08
    pops = np.zeros(n)
    for _ in range(90):
        at = rng.integers(0, n - 800)
        length = rng.integers(60, 700)
        pop = rng.normal(0, 1, length) * np.exp(-np.arange(length) / (length / 5)) * rng.uniform(0.2, 1)
        pops[at:at + length] += pop
    pops = bandpass(pops, 700, 6000)
    loop = rumble * 0.5 + hiss + pops * 0.6
    fade = int(SR * 0.1)
    loop[:fade] *= np.linspace(0, 1, fade) ** 0.5
    loop[-fade:] *= np.linspace(1, 0, fade) ** 0.5
    return loop


FORMANTS = {"a": [(800, 80), (1150, 90), (2900, 120)], "u": [(350, 60), (600, 60), (2400, 100)], "o": [(450, 70), (800, 80), (2830, 100)]}


def voice_glottal(rng, n, pitch_curve):
    phase = np.cumsum(pitch_curve) / SR
    saw = 2 * (phase % 1) - 1
    return saw + rng.normal(0, 0.15, n)


def formant(x, vowel):
    out = np.zeros_like(x)
    for f, bw in FORMANTS[vowel]:
        out += bandpass(x, f - bw, f + bw, order=1)
    return out


def crowd(rng, seconds, voices, vowels, pitch_lo, pitch_hi, rise, distance):
    n = int(SR * seconds)
    t = np.arange(n) / SR
    out = np.zeros(n)
    for _ in range(voices):
        start = int(rng.uniform(0, 0.25) * SR)
        length = n - start
        tt = t[:length]
        base = rng.uniform(pitch_lo, pitch_hi)
        curve = base * (1 + rise * np.minimum(1, tt / 0.35)) * (1 + 0.02 * np.sin(2 * np.pi * rng.uniform(4, 7) * tt))
        src = voice_glottal(rng, length, curve)
        if len(vowels) == 1:
            v = formant(src, vowels[0])
        else:
            split = int(length * 0.3)
            v = np.concatenate([formant(src[:split], vowels[0]), formant(src[split:], vowels[1])])
        out[start:] += np.tanh(v * 3) * env(length, 0.06, seconds * 0.5, 1.5) * rng.uniform(0.6, 1)
    out += bandpass(rng.normal(0, 1, n), 500, 4000) * env(n, 0.1, seconds * 0.5) * 0.3 * voices / 10
    return room(lowpass(out, 3500 - distance * 2000), 0.3 + distance * 0.5)


# ------------------------------------------------------------------ voices
def render_voices(tmp):
    manifest = []
    for key, texts in LINES.items():
        for i, text in enumerate(texts):
            count = 3 if key in GROUP_SHOUTS else 1
            for v in range(count):
                voice = VOICES[(i + v) % len(VOICES)]
                manifest.append({"file": os.path.join(tmp, f"{key}_{i}_{v}.wav"), "voice": voice, "text": text,
                                 "rate": 1 if key != "voice_rally" else 0, "pitch": "low" if v == 0 else "x-low"})
    path = os.path.join(tmp, "lines.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(manifest, f)
    shell = shutil.which("pwsh") or shutil.which("powershell")
    subprocess.run([shell, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", os.path.join(os.path.dirname(__file__), "speak.ps1"), "-Manifest", path], check=True)
    return manifest


def shout(parts, rng):
    """Turns TTS takes into a shout: lower pitch, trimmed, gritty, layered with slight delays, a bit of room."""
    mixed = None
    for k, part in enumerate(parts):
        x = part
        # Trim silence.
        idx = np.where(np.abs(x) > 0.02)[0]
        if idx.size:
            x = x[max(0, idx[0] - 200): idx[-1] + 400]
        # Pitch and speed down a little (tape-style), more for the backing voices.
        factor = 1.08 + 0.05 * k
        x = signal.resample(x, int(len(x) * factor))
        x = np.tanh(x * (4 + k)) * 0.8
        x = lowpass(x, 5000)
        delay = int(SR * (0.03 * k + rng.uniform(0, 0.02)))
        x = np.concatenate([np.zeros(delay), x * (1 if k == 0 else 0.55)])
        if mixed is None:
            mixed = x
        else:
            if len(x) > len(mixed):
                mixed = np.concatenate([mixed, np.zeros(len(x) - len(mixed))])
            mixed[: len(x)] += x
    mixed = np.concatenate([mixed, np.zeros(int(SR * 0.12))])
    return room(mixed, 0.3) * env(len(mixed), 0.005, 0.08, 1.0)


# ------------------------------------------------------------------ main
def find_kenney(folder, pack, stem):
    for dirpath, _, files in os.walk(folder):
        if pack == "rpg" and "rpg" not in dirpath.lower():
            continue
        if pack == "impact" and "impact" not in dirpath.lower():
            continue
        for f in files:
            if os.path.splitext(f)[0] == stem and f.endswith(".ogg"):
                return os.path.join(dirpath, f)
    raise FileNotFoundError(f"{pack}:{stem}")


def register(entries):
    path = os.path.join(CONTENT, "Content.mgcb")
    text = open(path, encoding="utf-8").read()
    marker = "# Sound: generated by tools/audio/build_audio.py"
    if marker in text:
        text = text[: text.index(marker)].rstrip() + "\n"
    blocks = [f"\n{marker} (Kenney CC0, synthesized, and placeholder TTS voices)\n"]
    for rel in entries:
        importer = "OggImporter" if rel.endswith(".ogg") else "WavImporter"
        blocks.append(f"#begin {rel}\n/importer:{importer}\n/processor:SoundEffectProcessor\n/processorParam:Quality=Medium\n/build:{rel}\n\n")
    open(path, "w", encoding="utf-8").write(text + "".join(blocks))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--kenney", required=True, help="folder containing the unzipped Kenney RPG Audio and Impact Sounds packs")
    parser.add_argument("--skip-voices", action="store_true")
    args = parser.parse_args()
    rng = np.random.default_rng(20260917)

    if os.path.isdir(AUDIO):
        shutil.rmtree(AUDIO)
    entries = []

    for key, sources in KENNEY.items():
        for i, src in enumerate(sources):
            pack, stem = src.split(":")
            rel = f"Audio/Sfx/{key}_{i}.ogg"
            os.makedirs(os.path.join(CONTENT, "Audio", "Sfx"), exist_ok=True)
            shutil.copyfile(find_kenney(args.kenney, pack, stem), os.path.join(CONTENT, rel))
            entries.append(rel)

    synth = {
        "bow": [bow_twang(rng, p) for p in (140, 165, 190)],
        "whoosh": [whoosh(rng) for _ in range(3)],
        "horn_own": [horn(rng, 146.8, 1.6, rough=False)],
        "horn_enemy": [horn(rng, 110.0, 2.2, rough=True)],
        "drums": [marching_drums(rng)],
        "fire": [fire_loop(rng)],
        "roar_enemy": [crowd(rng, 1.8, 14, ["a"], 95, 150, 0.15, distance=0.6) for _ in range(3)],
        "cheer": [crowd(rng, 1.4, 12, ["u", "a"], 120, 190, 0.25, distance=0.2) for _ in range(2)],
    }
    for key, clips in synth.items():
        for i, clip in enumerate(clips):
            rel = f"Audio/Sfx/{key}_{i}.wav"
            write_wav(os.path.join(CONTENT, rel), clip)
            entries.append(rel)

    if not args.skip_voices:
        with tempfile.TemporaryDirectory() as tmp:
            manifest = render_voices(tmp)
            takes = {}
            for m in manifest:
                key, line, _ = re.match(r"(.+)_(\d+)_(\d+)\.wav$", os.path.basename(m["file"])).groups()
                takes.setdefault((key, int(line)), []).append(read_wav(m["file"]))
            for (key, line), parts in sorted(takes.items()):
                rel = f"Audio/Voice/{key}_{line}.wav"
                write_wav(os.path.join(CONTENT, rel), shout(parts, rng))
                entries.append(rel)

    register(entries)
    with open(os.path.join(AUDIO, "License.txt"), "w", encoding="utf-8") as f:
        f.write("Sfx/*.ogg: Kenney 'RPG Audio' and 'Impact Sounds', CC0 (https://kenney.nl).\n"
                "Sfx/*.wav: synthesized by tools/audio/build_audio.py for this project.\n"
                "Voice/*.wav: placeholder shouts rendered with the Windows speech synthesizer by tools/audio/build_audio.py.\n"
                "Replace Voice/ with real recordings (same file names) before distributing the game.\n")
    print(f"{len(entries)} sounds written to {AUDIO}")


if __name__ == "__main__":
    sys.exit(main())
