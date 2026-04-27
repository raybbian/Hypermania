"""Generate repository metrics for Hypermania.

Walks the git history and Unity assets to produce charts (PNG) and a
markdown summary in ./output.

Run from the venv:

    venv/Scripts/python generate_metrics.py
"""

from __future__ import annotations

import datetime as dt
import re
import subprocess
from collections import Counter, defaultdict
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.dates as mdates
import matplotlib.pyplot as plt

SCRIPT_DIR = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIR.parent
ASSETS = REPO_ROOT / "Hypermania" / "Assets"
SCRIPTS_DIR = ASSETS / "Scripts"
CHARACTERS_DIR = ASSETS / "Characters"
OUT_DIR = SCRIPT_DIR / "output"
OUT_DIR.mkdir(parents=True, exist_ok=True)

KIND_NAMES = {0: "Hurtbox", 1: "Hitbox", 2: "Grabbox"}
KIND_COLORS = {"Hurtbox": "#4ea3ff", "Hitbox": "#ff5c5c", "Grabbox": "#ffce4e"}

EXCLUDE_DIR_PARTS = {"Library", "Packages", "PackageCache"}


def run_git(args: list[str]) -> str:
    return subprocess.run(
        ["git", *args],
        cwd=REPO_ROOT,
        check=True,
        capture_output=True,
        text=True,
        encoding="utf-8",
    ).stdout


def get_commits() -> list[tuple[str, dt.datetime, str, str]]:
    raw = run_git(["log", "--pretty=format:%H|%aI|%an|%s"])
    out: list[tuple[str, dt.datetime, str, str]] = []
    for line in raw.splitlines():
        if not line.strip():
            continue
        parts = line.split("|", 3)
        if len(parts) != 4:
            continue
        h, iso, author, subject = parts
        ts = dt.datetime.fromisoformat(iso).astimezone()
        out.append((h, ts, author, subject))
    return out


def excluded(path: Path) -> bool:
    return any(part in EXCLUDE_DIR_PARTS for part in path.parts)


def find_animations() -> list[Path]:
    return [p for p in ASSETS.rglob("*.anim") if not excluded(p)]


def find_animation_assets() -> list[Path]:
    return [
        p
        for p in ASSETS.rglob("*.asset")
        if not excluded(p) and "Animations" in p.parts
    ]


KIND_RE = re.compile(r"^\s+Kind: (\d+)\s*$", re.M)


def count_hitboxes(asset_path: Path) -> Counter[str]:
    text = asset_path.read_text(encoding="utf-8", errors="ignore")
    c: Counter[str] = Counter()
    for m in KIND_RE.finditer(text):
        n = int(m.group(1))
        c[KIND_NAMES.get(n, f"Unknown({n})")] += 1
    return c


def character_of(path: Path) -> str:
    try:
        rel = path.relative_to(CHARACTERS_DIR)
        return rel.parts[0]
    except ValueError:
        return "Shared/VFX"


BLOCK_COMMENT_RE = re.compile(r"/\*.*?\*/", re.S)
LINE_COMMENT_RE = re.compile(r"//.*$")


def code_lines(p: Path) -> int:
    try:
        text = p.read_text(encoding="utf-8", errors="ignore")
    except OSError:
        return 0
    text = BLOCK_COMMENT_RE.sub("", text)
    n = 0
    for line in text.splitlines():
        stripped = LINE_COMMENT_RE.sub("", line).strip()
        if stripped:
            n += 1
    return n


def find_cs_files() -> list[Path]:
    return [p for p in SCRIPTS_DIR.rglob("*.cs") if not excluded(p)]


def loc_dir_bucket(p: Path) -> str:
    rel = p.relative_to(SCRIPTS_DIR)
    parts = rel.parts
    if len(parts) <= 1:
        return "(root)"
    if parts[0] in ("Game", "Design") and len(parts) >= 3:
        return f"{parts[0]}/{parts[1]}"
    return parts[0]


def save(fig, name: str) -> Path:
    path = OUT_DIR / name
    fig.savefig(path, dpi=120, bbox_inches="tight")
    plt.close(fig)
    return path


def plot_commits_by_hour(commits, filename="commits_by_hour.png", title_suffix="") -> int:
    counts = [0] * 24
    for _, ts, _, _ in commits:
        counts[ts.hour] += 1
    fig, ax = plt.subplots(figsize=(10, 4))
    ax.bar(range(24), counts, color="#7c4dff")
    ax.set_xticks(range(24))
    ax.set_xlabel("Hour of day (local)")
    ax.set_ylabel("Commits")
    title = f"Commits by hour of day{title_suffix}  (n={len(commits)})"
    ax.set_title(title)
    ax.grid(axis="y", alpha=0.3)
    save(fig, filename)
    return len(commits)


def plot_commits_by_weekday(commits) -> None:
    names = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"]
    counts = [0] * 7
    for _, ts, _, _ in commits:
        counts[ts.weekday()] += 1
    fig, ax = plt.subplots(figsize=(7, 4))
    ax.bar(names, counts, color="#26a69a")
    ax.set_ylabel("Commits")
    ax.set_title("Commits by day of week")
    ax.grid(axis="y", alpha=0.3)
    save(fig, "commits_by_weekday.png")


def plot_commits_over_time(commits) -> None:
    if not commits:
        return
    by_week: Counter[dt.date] = Counter()
    for _, ts, _, _ in commits:
        monday = (ts - dt.timedelta(days=ts.weekday())).date()
        by_week[monday] += 1
    weeks = sorted(by_week)
    counts = [by_week[w] for w in weeks]
    fig, ax = plt.subplots(figsize=(11, 4))
    ax.bar(weeks, counts, color="#ff7043", width=6)
    ax.xaxis.set_major_locator(mdates.MonthLocator())
    ax.xaxis.set_major_formatter(mdates.DateFormatter("%b %Y"))
    ax.set_ylabel("Commits per week")
    ax.set_title("Commit cadence")
    ax.grid(axis="y", alpha=0.3)
    fig.autofmt_xdate()
    save(fig, "commits_over_time.png")


def plot_commits_by_author(commits) -> None:
    c = Counter(a for _, _, a, _ in commits)
    items = c.most_common(10)
    names = [n for n, _ in items][::-1]
    vals = [v for _, v in items][::-1]
    fig, ax = plt.subplots(figsize=(8, max(3, 0.45 * len(items) + 1)))
    ax.barh(names, vals, color="#42a5f5")
    ax.set_xlabel("Commits")
    ax.set_title(f"Commits by author  (top {len(items)})")
    ax.grid(axis="x", alpha=0.3)
    for i, v in enumerate(vals):
        ax.text(v, i, f" {v}", va="center")
    save(fig, "commits_by_author.png")


def plot_animations_by_character(anim_buckets: Counter[str]) -> None:
    items = sorted(anim_buckets.items(), key=lambda kv: kv[1], reverse=True)
    names = [k for k, _ in items]
    vals = [v for _, v in items]
    fig, ax = plt.subplots(figsize=(7, 4))
    bars = ax.bar(names, vals, color="#ab47bc")
    ax.set_ylabel("Animation count (.anim files)")
    ax.set_title(f"Animations by character  (total {sum(vals)})")
    for b, v in zip(bars, vals):
        ax.text(b.get_x() + b.get_width() / 2, v, str(v), ha="center", va="bottom")
    save(fig, "animations_by_character.png")


def plot_hitboxes_by_character(hitbox_by_char: dict[str, Counter[str]]) -> None:
    chars = sorted(hitbox_by_char, key=lambda c: -sum(hitbox_by_char[c].values()))
    kinds = ["Hurtbox", "Hitbox", "Grabbox"]
    fig, ax = plt.subplots(figsize=(8, 4.5))
    bottom = [0] * len(chars)
    for kind in kinds:
        vals = [hitbox_by_char[c].get(kind, 0) for c in chars]
        ax.bar(chars, vals, bottom=bottom, label=kind, color=KIND_COLORS[kind])
        bottom = [b + v for b, v in zip(bottom, vals)]
    ax.set_ylabel("Hitbox entries")
    ax.set_title(f"Hitboxes by character  (total {sum(bottom)})")
    ax.legend()
    for i, total in enumerate(bottom):
        ax.text(i, total, str(total), ha="center", va="bottom")
    save(fig, "hitboxes_by_character.png")


def plot_loc_by_directory(loc_by_dir: Counter[str]) -> None:
    items = sorted(loc_by_dir.items(), key=lambda kv: kv[1], reverse=True)
    names = [k for k, _ in items]
    vals = [v for _, v in items]
    fig, ax = plt.subplots(figsize=(9, max(3, 0.45 * len(names) + 1)))
    ax.barh(names[::-1], vals[::-1], color="#66bb6a")
    ax.set_xlabel("Lines of C# code (non-blank, non-comment)")
    ax.set_title(f"LOC by directory  (total {sum(vals)})")
    ax.grid(axis="x", alpha=0.3)
    save(fig, "loc_by_directory.png")


def main() -> None:
    print("[1/4] Reading git history...")
    commits = get_commits()

    print("[2/4] Walking assets...")
    anims = find_animations()
    anim_buckets: Counter[str] = Counter(character_of(p) for p in anims)
    asset_files = find_animation_assets()
    hitbox_by_char: dict[str, Counter[str]] = defaultdict(Counter)
    total_hitbox: Counter[str] = Counter()
    for ap in asset_files:
        c = count_hitboxes(ap)
        if not c:
            continue
        hitbox_by_char[character_of(ap)] += c
        total_hitbox += c

    print("[3/4] Counting C# code...")
    cs_files = find_cs_files()
    file_loc = {p: code_lines(p) for p in cs_files}
    loc_by_dir: Counter[str] = Counter()
    for p, n in file_loc.items():
        loc_by_dir[loc_dir_bucket(p)] += n
    total_loc = sum(file_loc.values())
    biggest = sorted(file_loc.items(), key=lambda kv: kv[1], reverse=True)[:5]

    print("[4/4] Rendering charts...")
    plot_commits_by_hour(commits)
    raybbian_commits = [c for c in commits if c[2] == "Raymond Bian"]
    plot_commits_by_hour(
        raybbian_commits,
        filename="commits_by_hour_raybbian.png",
        title_suffix=" — Raymond Bian",
    )
    plot_commits_by_weekday(commits)
    plot_commits_over_time(commits)
    plot_commits_by_author(commits)
    plot_animations_by_character(anim_buckets)
    plot_hitboxes_by_character(hitbox_by_char)
    plot_loc_by_directory(loc_by_dir)

    head = run_git(["rev-parse", "--short", "HEAD"]).strip()
    now_local = dt.datetime.now().astimezone()
    tz = now_local.tzname() or "local"
    generated = now_local.strftime("%Y-%m-%d %H:%M %Z")
    contributors = sorted({a for _, _, a, _ in commits})

    if commits:
        first = min(c[1] for c in commits).date()
        last = max(c[1] for c in commits).date()
        span_days = (last - first).days + 1
        avg_per_week = len(commits) / max(span_days / 7, 1)
    else:
        first = last = dt.date.today()
        span_days = 0
        avg_per_week = 0.0

    md: list[str] = []
    md.append("# Hypermania metrics\n")
    md.append(
        f"_Generated {generated} from `{head}`. "
        f"Commit times shown in **{tz}**._\n"
    )

    md.append("## Headline numbers\n")
    md.append(f"- **Commits:** {len(commits)} "
              f"({first.isoformat()} → {last.isoformat()}, "
              f"{span_days} days, ~{avg_per_week:.1f} commits/week)")
    md.append(f"- **Contributors:** {len(contributors)} "
              f"({'; '.join(contributors)})")
    md.append(f"- **Animations (.anim files):** {sum(anim_buckets.values())}")
    md.append(
        f"- **Hitbox entries:** {sum(total_hitbox.values())} "
        f"(Hurtbox {total_hitbox['Hurtbox']}, "
        f"Hitbox {total_hitbox['Hitbox']}, "
        f"Grabbox {total_hitbox['Grabbox']})"
    )
    md.append(f"- **C# files:** {len(cs_files)}")
    md.append(f"- **Lines of code (non-blank, non-comment):** {total_loc}\n")

    md.append("## Per-character breakdown\n")
    md.append("| Character | Animations | Hurtboxes | Hitboxes | Grabboxes |")
    md.append("|---|---:|---:|---:|---:|")
    char_keys = sorted(set(anim_buckets) | set(hitbox_by_char))
    for c in char_keys:
        h = hitbox_by_char.get(c, Counter())
        md.append(
            f"| {c} | {anim_buckets.get(c, 0)} | "
            f"{h.get('Hurtbox', 0)} | {h.get('Hitbox', 0)} | {h.get('Grabbox', 0)} |"
        )
    md.append("")

    md.append("## Largest C# files\n")
    md.append("| File | LOC |")
    md.append("|---|---:|")
    for p, n in biggest:
        md.append(f"| `{p.relative_to(REPO_ROOT).as_posix()}` | {n} |")
    md.append("")

    md.append("## Charts\n")
    for fn, title in [
        ("commits_by_hour.png", "Commits by hour of day"),
        ("commits_by_hour_raybbian.png", "Commits by hour of day — Raymond Bian"),
        ("commits_by_weekday.png", "Commits by day of week"),
        ("commits_over_time.png", "Commit cadence over time"),
        ("commits_by_author.png", "Commits by author"),
        ("animations_by_character.png", "Animations by character"),
        ("hitboxes_by_character.png", "Hitboxes by character"),
        ("loc_by_directory.png", "Lines of code by directory"),
    ]:
        md.append(f"### {title}\n")
        md.append(f"![{title}]({fn})\n")

    (OUT_DIR / "metrics.md").write_text("\n".join(md), encoding="utf-8")
    print(f"Wrote {OUT_DIR / 'metrics.md'} and {len(list(OUT_DIR.glob('*.png')))} PNGs.")


if __name__ == "__main__":
    main()
