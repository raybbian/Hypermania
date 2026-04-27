# Hypermania.Scripts

One-off Python tooling for the repo. Output is gitignored under `output/`.

## Setup

```bash
python -m venv venv
venv/Scripts/pip install matplotlib
```

## Generate metrics

```bash
venv/Scripts/python generate_metrics.py
```

Produces `output/metrics.md` plus seven PNG charts:

- `commits_by_hour.png` — when of day commits land (local time)
- `commits_by_weekday.png` — Mon–Sun split
- `commits_over_time.png` — weekly cadence
- `commits_by_author.png` — top contributors
- `animations_by_character.png` — `.anim` count per character
- `hitboxes_by_character.png` — Hurtbox / Hitbox / Grabbox stacked
- `loc_by_directory.png` — non-blank, non-comment C# lines per top-level subdir
