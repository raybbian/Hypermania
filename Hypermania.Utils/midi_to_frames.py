"""Parse a named MIDI track into an array of frame indices.

Each note_on event in the requested track is mapped to the nearest frame
index at the target framerate (default 60 fps), using the tempo(s)
declared in the MIDI file itself. Output is printed as a list literal
so it can be pasted directly into configs.

Usage:
    python midi_to_frames.py <midi_path> <track_name> [--fps 60] [--output out.txt]
"""

import argparse
import sys

import mido


def find_track(mid: mido.MidiFile, track_name: str) -> mido.MidiTrack:
    available = []
    for track in mid.tracks:
        name = None
        for msg in track:
            if msg.is_meta and msg.type == "track_name":
                name = msg.name
                break
        if name is not None:
            available.append(name)
        if name == track_name:
            return track

    raise SystemExit(
        f"Track '{track_name}' not found. Available tracks: {available}"
    )


def track_to_frames(mid: mido.MidiFile, track: mido.MidiTrack, fps: int) -> list[int]:
    ticks_per_beat = mid.ticks_per_beat
    current_tempo = 500_000  # default 120 BPM per MIDI spec

    # Seed current_tempo with the first set_tempo found anywhere in the file
    # before the target track starts, so tracks that don't carry their own
    # tempo still pick up the file-level BPM.
    for t in mid.tracks:
        for msg in t:
            if msg.is_meta and msg.type == "set_tempo":
                current_tempo = msg.tempo
                break
        else:
            continue
        break

    elapsed_seconds = 0.0
    frames: list[int] = []

    for msg in track:
        if msg.time:
            elapsed_seconds += mido.tick2second(
                msg.time, ticks_per_beat, current_tempo
            )

        if msg.is_meta and msg.type == "set_tempo":
            current_tempo = msg.tempo
            continue

        if msg.type == "note_on" and msg.velocity > 0:
            frames.append(round(elapsed_seconds * fps))

    return frames


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("midi_path", help="Path to the .mid file")
    parser.add_argument("track_name", help="Exact name of the track to extract")
    parser.add_argument("--fps", type=int, default=60, help="Target framerate (default 60)")
    parser.add_argument(
        "--output",
        default=None,
        help="Optional output file. Defaults to stdout.",
    )
    args = parser.parse_args()

    mid = mido.MidiFile(args.midi_path)
    track = find_track(mid, args.track_name)
    frames = track_to_frames(mid, track, args.fps)

    rendered = "[" + ", ".join(str(f) for f in frames) + "]"

    if args.output:
        with open(args.output, "w", encoding="utf-8") as fh:
            fh.write(rendered + "\n")
    else:
        sys.stdout.write(rendered + "\n")


if __name__ == "__main__":
    main()
