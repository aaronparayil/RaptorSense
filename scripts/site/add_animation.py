"""Puts animations people share through the "Share an animation" issue form on the website, or takes them off.

Run by .github/workflows/share-animation.yml. Nobody has to approve an animation: when an issue is opened or edited,
this checks it and, if it passes, adds it. Reads everything from the environment, never from the command line, so
nothing in an issue can become a command:

  MODE             "add" (default) or "remove" (the owner labelled the issue "remove animation")
  ISSUE_BODY       the issue form's text
  ISSUE_NUMBER     the issue
  ISSUE_AUTHOR     the GitHub login of whoever shared it
  ACCOUNT_CREATED  when that account was made (ISO 8601), for the new-account rule
  OVERRIDE         "1" when the owner labelled it "approved animation": skips the hold rules and the daily limit,
                   never the checks on the file itself

The file is cleaned the way the app cleans an import (KeyAnimation.Cleaned): known keys, valid colours, times in range,
at most 500 frames. Then one of three things happens, written to the step output "outcome" and explained in result.md:

  added    it is in site/animations and index.json
  refused  something the sharer can fix: not an animation, all dark, flashes more than three times a second (the rule
           RaptorSense's own effects follow), a copy of one already up, or over the limit of 3 a day
  held     it waits for the owner: the name, credit or description has a word from held-words.txt or a link, or the
           GitHub account is under 3 days old
  removed  (MODE=remove) the animations added from this issue are gone

Exits with 0 for all of those, and with 1 only if something unexpected broke.

Local try:  ISSUE_BODY="$(cat body.md)" ISSUE_NUMBER=1 ISSUE_AUTHOR=someone python3 scripts/site/add_animation.py
"""
import codecs
import datetime
import json
import math
import os
import re
import sys
import urllib.request

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SITE = os.path.join(ROOT, "site")
ANIMATIONS = os.path.join(SITE, "animations")
INDEX = os.path.join(ANIMATIONS, "index.json")
RESULT = os.path.join(ROOT, "result.md")
PAGE = "https://aaronparayil.github.io/RaptorSense/animations.html"

MAX_BYTES = 5 * 1024 * 1024        # AnimationStore.MaxImportBytes
MAX_FRAMES = 500                   # KeyAnimation.MaxFrames
MAX_HOLD, MAX_FADE = 60.0, 30.0    # AnimationFrame.MaxHold, MaxFade
MIN_FRAME_SECONDS = 1 / 60         # CompiledAnimation.MinFrameSeconds
MAX_NAME, MAX_AUTHOR, MAX_ABOUT = 60, 40, 200
PER_DAY = 3
NEW_ACCOUNT_DAYS = 3
ATTACHMENT = re.compile(r"https://github\.com/user-attachments/files/\d+/[^\s)\]]+")
LINKISH = re.compile(r"https?://|www\.|\.(com|net|org|gg|io|xyz|ru|me|tk|ly|in|co|app|link|shop)\b|discord|telegram|"
                     r"\bt\.me\b|whatsapp|@\w|\d{7,}", re.I)
LEET = str.maketrans("013457@$!|", "oieastasii")


class Refused(Exception):
    """A reason the animation can't go on the site that the sharer can fix, worded for them."""


class Held(Exception):
    """A reason it waits for the owner instead of going straight up."""


def finish(outcome, message):
    with open(RESULT, "w", encoding="utf-8") as f:
        f.write(message + "\n")
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as f:
            f.write(f"outcome={outcome}\n")
    print(f"{outcome}: {message}")
    sys.exit(0)


def clean_text(text, limit):
    text = "".join(ch for ch in (text or "") if ch.isprintable()).strip()
    text = text.replace("`", "'")
    return text[:limit].strip()


def form_fields(body):
    """The issue form's answers, by heading: '### Animation name' then the answer."""
    fields, heading, lines = {}, None, []
    for line in (body or "").replace("\r\n", "\n").split("\n"):
        if line.startswith("### "):
            if heading is not None:
                fields[heading] = "\n".join(lines).strip()
            heading, lines = line[4:].strip(), []
        elif heading is not None:
            lines.append(line)
    if heading is not None:
        fields[heading] = "\n".join(lines).strip()
    return {k: ("" if v == "_No response_" else v) for k, v in fields.items()}


def read_file_text(answer):
    """The animation's JSON: from a file dropped into the form, or pasted as text."""
    link = ATTACHMENT.search(answer)
    if link:
        request = urllib.request.Request(link.group(0), headers={"User-Agent": "raptorsense-site"})
        with urllib.request.urlopen(request, timeout=30) as response:
            if not response.geturl().startswith(("https://github.com/", "https://objects.githubusercontent.com/",
                                                 "https://github-production-user-asset-")):
                raise Refused("The file link didn't lead to a GitHub upload.")
            data = response.read(MAX_BYTES + 1)
        if len(data) > MAX_BYTES:
            raise Refused("The file is too big to be a RaptorSense animation.")
        return data.decode("utf-8-sig", errors="replace")
    text = answer.strip()
    fence = re.match(r"^```[a-zA-Z]*\n(.*)\n```$", text, re.S)
    if fence:
        text = fence.group(1)
    if len(text.encode("utf-8")) > MAX_BYTES:
        raise Refused("The text is too big to be a RaptorSense animation.")
    return text


def load_layout():
    with open(os.path.join(SITE, "data", "keyboard.json"), encoding="utf-8") as f:
        return json.load(f)["keys"]


def load_index():
    with open(INDEX, encoding="utf-8") as f:
        return json.load(f)


def save_index(index):
    with open(INDEX, "w", encoding="utf-8", newline="\n") as f:
        json.dump(index, f, indent=2, ensure_ascii=False)
        f.write("\n")


def parse_colour(value):
    """Rgb.TryParse: six hex digits, '#' allowed in front."""
    if not isinstance(value, str):
        return None
    hex_ = value.strip().lstrip("#")
    if len(hex_) != 6 or any(c not in "0123456789abcdefABCDEF" for c in hex_):
        return None
    v = int(hex_, 16)
    return (v >> 16 & 255, v >> 8 & 255, v & 255)


def clean_seconds(value, limit):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
        return 0.0
    return min(max(float(value), 0.0), limit)


def clean_animation(data, labels):
    """KeyAnimation.Cleaned, in the same key order and spelling, so the app reads back exactly this."""
    if not isinstance(data, dict):
        raise Refused("That isn't a RaptorSense animation file.")
    frames_in = data.get("Frames")
    if not isinstance(frames_in, list) or not any(isinstance(f, dict) for f in frames_in):
        raise Refused("That isn't a RaptorSense animation file: it has no frames.")
    by_lower = {label.lower(): label for label in labels}
    frames = []
    for frame in [f for f in frames_in if isinstance(f, dict)][:MAX_FRAMES]:
        hold = frame.get("Hold", 0.5)
        fade = frame.get("Fade", 0)
        for name, value in (("Hold", hold), ("Fade", fade)):
            if not isinstance(value, (int, float)) or isinstance(value, bool):
                raise Refused(f"A frame's {name} isn't a number.")
        cells = {}
        keys = frame.get("Keys") or {}
        if not isinstance(keys, dict):
            raise Refused("A frame's keys aren't a list of key colours.")
        for label, hex_ in keys.items():
            known = by_lower.get(str(label).lower())
            colour = parse_colour(hex_)
            if known and colour:
                cells[known] = colour
        frames.append({
            "Hold": clean_seconds(hold, MAX_HOLD),
            "Fade": clean_seconds(fade, MAX_FADE),
            "Keys": {label: "%02X%02X%02X" % cells[label] for label in labels if label in cells and cells[label] != (0, 0, 0)},
        })
    music = str(data.get("Music") or "").strip().lower()
    return frames, (music if music in ("beat", "speed", "pulse") else None)


def luminance(rgb):
    def linear(v):
        s = v / 255
        return s / 12.92 if s <= 0.04045 else ((s + 0.055) / 1.055) ** 2.4
    r, g, b = rgb
    return 0.2126 * linear(r) + 0.7152 * linear(g) + 0.0722 * linear(b)


def flash_count(frames, labels):
    """Most brightness swings of a tenth or more within any second (CompiledAnimation.Render, CollectionTests)."""
    colours = [[parse_colour(f["Keys"].get(label, "000000")) for label in labels] for f in frames]
    fades = [f["Fade"] for f in frames]
    holds = [max(f["Hold"], MIN_FRAME_SECONDS - f["Fade"]) for f in frames]
    starts, at = [], 0.0
    for h, d in zip(holds, fades):
        starts.append(at)
        at += h + d
    length = at

    def level(t):
        t %= length
        i = max(i for i, s in enumerate(starts) if s <= t)
        into = t - starts[i]
        a = colours[i]
        if into <= holds[i] or fades[i] <= 0:
            mixed = a
        else:
            b = colours[(i + 1) % len(colours)]
            k = min(max((into - holds[i]) / fades[i], 0), 1)
            mixed = [tuple(int(round(x + (y - x) * k)) for x, y in zip(p, q)) for p, q in zip(a, b)]
        return sum(luminance(c) for c in mixed) / len(mixed)

    seconds = min(max(length * 2, 4), 60)
    levels = [level(i / 60) for i in range(int(seconds * 60))]
    turns, extreme, direction = [], levels[0], 0
    for i in range(1, len(levels)):
        change = levels[i] - extreme
        if (direction >= 0 and change <= -0.1) or (direction <= 0 and change >= 0.1):
            turns.append(i)
            direction = 1 if change > 0 else -1
            extreme = levels[i]
        elif (direction > 0 and levels[i] > extreme) or (direction < 0 and levels[i] < extreme):
            extreme = levels[i]
    return max((sum(1 for t in turns if s <= t < s + 60) for s in turns), default=0)


def held_words():
    with open(os.path.join(os.path.dirname(__file__), "held-words.txt"), encoding="utf-8") as f:
        return {codecs.decode(line.strip(), "rot13") for line in f if line.strip() and not line.startswith("#")}


def needs_a_look(*texts):
    """Why a person should read this text before it goes up, or None. Errs on holding: a person decides after."""
    words = held_words()
    for text in texts:
        if LINKISH.search(text):
            return "it has something that looks like a link, a handle or a phone number in it"
        plain = text.lower().translate(LEET)
        tokens = re.findall(r"[a-z]+", plain)
        squashed = "".join(tokens)
        # Whole words, words starting with one ("...ty", "...ing"), and longer ones hidden inside run-together text.
        if (any(t in words for t in tokens)
                or any(len(w) >= 4 and t.startswith(w) for t in tokens for w in words)
                or any(len(w) >= 4 and w in squashed for w in words)):
            return "a word in it needs a person to look at it"
    return None


def same_frames(a, b):
    return json.dumps(a, sort_keys=True) == json.dumps(b, sort_keys=True)


def file_name_for(name, taken):
    stem = re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-")[:40].strip("-") or "animation"
    candidate, n = stem, 2
    while candidate + ".json" in taken:
        candidate, n = f"{stem}-{n}", n + 1
    return candidate + ".json"


def add():
    number = os.environ.get("ISSUE_NUMBER", "")
    login = clean_text(os.environ.get("ISSUE_AUTHOR", ""), MAX_AUTHOR)
    override = os.environ.get("OVERRIDE") == "1"
    fields = form_fields(os.environ.get("ISSUE_BODY", ""))
    file_answer = fields.get("Animation file", "")
    if not file_answer:
        raise Refused("The form has no animation file in it.")
    try:
        data = json.loads(read_file_text(file_answer))
    except (ValueError, UnicodeDecodeError):
        raise Refused("That isn't a RaptorSense animation file: it isn't readable JSON. "
                      "Export it from the Animations tab and drop the file in, or paste all of its text.")

    labels = [k["label"] for k in load_layout()]
    frames, music = clean_animation(data, labels)
    if not any(f["Keys"] for f in frames):
        raise Refused("Every frame is dark: no key is lit anywhere in it.")
    swings = flash_count(frames, labels)
    if swings > 6:
        raise Refused(f"It flashes too fast to show to everyone: {swings // 2} flashes within one second, and the limit "
                      "is 3 (the same rule as RaptorSense's own effects, for people who are sensitive to flashing). "
                      "Longer frames or fades fix it.")

    name = clean_text(fields.get("Animation name", ""), MAX_NAME) or clean_text(str(data.get("Name") or ""), MAX_NAME) or "Animation"
    author = clean_text(fields.get("Credit it to", ""), MAX_AUTHOR) or login or "Someone"
    about = clean_text(" ".join(fields.get("What it looks like", "").split()), MAX_ABOUT)

    index = load_index()
    for entry in index:
        if entry.get("issue") is not None and str(entry["issue"]) == number:
            finish("added", f"**{entry['name']}** is already on the website: {PAGE}#{entry['file'][:-5]}")
        with open(os.path.join(ANIMATIONS, entry["file"]), encoding="utf-8") as f:
            if same_frames(json.load(f)["Frames"], frames):
                raise Refused(f"It's the same as **{entry['name']}**, which is already on the website.")

    today = datetime.datetime.now(datetime.timezone.utc)
    if not override:
        if login and sum(1 for e in index if e.get("login") == login and e.get("added") == today.strftime("%Y-%m-%d")) >= PER_DAY:
            raise Refused(f"You've shared {PER_DAY} today, which is the most for one day. Edit this issue tomorrow "
                          "(any small change will do) and it will be checked again.")
        why = needs_a_look(name, author, about)
        if why:
            raise Held(f"Its name, credit or description will be read by a person first, because {why}.")
        created = os.environ.get("ACCOUNT_CREATED", "")
        try:
            age = today - datetime.datetime.fromisoformat(created.replace("Z", "+00:00"))
        except ValueError:
            age = None
        if age is None or age < datetime.timedelta(days=NEW_ACCOUNT_DAYS):
            raise Held(f"Your GitHub account is new (under {NEW_ACCOUNT_DAYS} days old), so a person looks at it first.")

    file = file_name_for(name, set(os.listdir(ANIMATIONS)))
    with open(os.path.join(ANIMATIONS, file), "w", encoding="utf-8", newline="\n") as f:
        json.dump({"Name": name, "Frames": frames, **({"Music": music} if music else {})}, f, indent=2)
        f.write("\n")
    entry = {"file": file, "name": name, "author": author, "about": about, "frames": len(frames), "music": music,
             "added": today.strftime("%Y-%m-%d"), "login": login}
    if number.isdigit():
        entry["issue"] = int(number)
    index.append(entry)
    save_index(index)
    slug = file[:-5]
    finish("added", f"Thanks! **{name}** is on the website now, credited to {author}: {PAGE}#{slug}\n\n"
           "It can take a minute or two to appear. Anyone with RaptorSense can add it with **Add to RaptorSense** on "
           "that page, or from **Shared** in the app's Animations tab.")


def remove():
    number = os.environ.get("ISSUE_NUMBER", "")
    index = load_index()
    gone = [e for e in index if str(e.get("issue", "")) == number]
    if not gone:
        finish("nothing", "There was no animation from this issue on the website.")
    for e in gone:
        path = os.path.join(ANIMATIONS, e["file"])
        if os.path.exists(path):
            os.remove(path)
    save_index([e for e in index if e not in gone])
    finish("removed", "This animation has been taken off the website.")


if __name__ == "__main__":
    try:
        remove() if os.environ.get("MODE") == "remove" else add()
    except Refused as reason:
        finish("refused", f"This animation couldn't be added: {reason}\n\n"
               "You're welcome to fix it: edit this issue (or share it again) and it's checked again straight away.")
    except Held as reason:
        finish("held", f"Thanks! This one is waiting for a quick look before it goes up. {reason}\n\n"
               "Nothing for you to do: you'll get a reply here when it's on the website.")
