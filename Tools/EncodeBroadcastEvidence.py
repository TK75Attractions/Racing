#!/usr/bin/env python3
"""Encode captured game frames with their actual wall-clock spacing; no fabricated motion."""
import argparse
import json
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('folder', type=Path)
parser.add_argument('--ffmpeg', default='/opt/homebrew/bin/ffmpeg')
parser.add_argument('--segment', choices=['both', 'live', 'replay'], default='both')
args = parser.parse_args()
for name, destination in [('LiveFrames', 'broadcast-live.mp4'), ('ReplayFrames', 'broadcast-replay.mp4')]:
    if args.segment != 'both' and ('live' if name == 'LiveFrames' else 'replay') != args.segment:
        continue
    source = args.folder / name
    frames = json.loads((source / 'timeline.json').read_text())['frames']
    if not frames:
        raise RuntimeError(f'No captured frames in {source}')
    lines = ['ffconcat version 1.0']
    for i, frame in enumerate(frames):
        filename = frame['file']
        if Path(filename).name != filename or not (source / filename).is_file():
            raise RuntimeError('Invalid capture filename')
        duration = frames[i + 1]['seconds'] - frame['seconds'] if i + 1 < len(frames) else 1 / 12
        lines += [f"file '{filename}'", f'duration {max(.001, duration):.6f}']
    lines += [f"file '{frames[-1]['file']}'"]
    listing = source / 'frames.ffconcat'
    listing.write_text('\n'.join(lines) + '\n')
    subprocess.run([args.ffmpeg, '-hide_banner', '-loglevel', 'warning', '-y', '-f', 'concat', '-safe', '1',
                    '-i', str(listing), '-vf', 'fps=24', '-an', '-c:v', 'libx264', '-preset', 'veryfast',
                    '-crf', '25', '-maxrate', '2000k', '-bufsize', '4000k', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(args.folder / destination)], check=True)
    print(f'{destination}: {len(frames)} real frames, {frames[-1]["seconds"] - frames[0]["seconds"]:.2f} seconds observed')
