// Draws the Predator Helios 16 keyboard and plays RaptorSense animation files on it, the way the app does
// (CompiledAnimation in src/RaptorSense.Core/Lighting/KeyAnimation.cs): frames hold, then fade into the next, looping.
// The key layout in data/keyboard.json is written from the app's own (tests/RaptorSense.Tests/SiteTests.cs).

const MAX_HOLD = 60, MAX_FADE = 30, MAX_FRAMES = 500, MIN_FRAME_SECONDS = 1 / 60;

// What is printed on the caps, where it differs from the key's name in the file (KeyboardView.CapText in the app).
const CAPS = {
  "`": "~ `", "-": "_ -", "=": "+ =", "[": "{ [", "]": "} ]", "\\": "| \\", ";": ": ;", "'": "\" '", ",": "< ,",
  ".": "> .", "/": "? /", Bksp: "Backspace", Caps: "Caps Lock", RShift: "Shift", RCtrl: "Ctrl", AltGr: "Alt",
  Up: "▲", Down: "▼", Left: "◀", Right: "▶", Prev: "⏮", Play: "⏯", Next: "⏭", Power: "⏻", Predator: "P", Menu: "≡",
  "Num/": "/", "Num*": "*", "Num-": "-", "Num+": "+", NumEnt: "Enter", "Num.": ".", NumLk: "Num",
  Num0: "0", Num1: "1", Num2: "2", Num3: "3", Num4: "4", Num5: "5", Num6: "6", Num7: "7", Num8: "8", Num9: "9",
};

let layoutPromise;
export function loadLayout() {
  layoutPromise ??= fetch(new URL("../data/keyboard.json", import.meta.url)).then(r => {
    if (!r.ok) throw new Error("keyboard layout missing");
    return r.json();
  });
  return layoutPromise;
}

function cleanSeconds(v, max) {
  return typeof v === "number" && Number.isFinite(v) ? Math.min(Math.max(v, 0), max) : 0;
}

function parseColour(hex) {
  if (typeof hex !== "string") return null;
  const h = hex.trim().replace(/^#+/, "");
  return /^[0-9a-fA-F]{6}$/.test(h) ? [parseInt(h.slice(0, 2), 16), parseInt(h.slice(2, 4), 16), parseInt(h.slice(4, 6), 16)] : null;
}

/** Turns an animation file into colour arrays, one RGB triple per key of the layout. */
export function compile(animation, layout) {
  const index = new Map(layout.keys.map((k, i) => [k.label.toLowerCase(), i]));
  const n = layout.keys.length;
  const frames = [], holds = [], fades = [], starts = [];
  let at = 0;
  for (const f of (animation?.Frames ?? []).filter(f => f && typeof f === "object").slice(0, MAX_FRAMES)) {
    const cells = new Uint8ClampedArray(n * 3);
    for (const [label, hex] of Object.entries(f.Keys ?? {})) {
      const i = index.get(String(label).toLowerCase());
      const c = parseColour(hex);
      if (i !== undefined && c) cells.set(c, i * 3);
    }
    const fade = cleanSeconds(f.Fade ?? 0, MAX_FADE);
    const hold = Math.max(cleanSeconds(f.Hold ?? 0.5, MAX_HOLD), MIN_FRAME_SECONDS - fade);
    frames.push(cells); holds.push(hold); fades.push(fade); starts.push(at);
    at += hold + fade;
  }
  return { frames, holds, fades, starts, length: at, keys: n };
}

/** The colours <time> seconds into the loop, written into out (keys * 3). */
export function render(c, time, out) {
  if (!c.frames.length || c.length <= 0) { out.fill(0); return out; }
  let t = time % c.length;
  if (t < 0) t += c.length;
  let lo = 0, hi = c.starts.length - 1;
  while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (c.starts[mid] <= t) lo = mid; else hi = mid - 1; }
  const i = lo, from = c.frames[i], into = t - c.starts[i];
  if (into <= c.holds[i] || c.fades[i] <= 0) { out.set(from); return out; }
  const to = c.frames[(i + 1) % c.frames.length], k = Math.min((into - c.holds[i]) / c.fades[i], 1);
  for (let j = 0; j < out.length; j++) out[j] = Math.round(from[j] + (to[j] - from[j]) * k);
  return out;
}

/** Draws the keyboard on a canvas. Big ones get key labels and a glow. */
export class KeyboardCanvas {
  constructor(canvas, layout, { labels = false, glow = false, pad = 0 } = {}) {
    this.canvas = canvas; this.layout = layout; this.labels = labels; this.glow = glow; this.pad = pad;
    this.ctx = canvas.getContext("2d");
    this.colours = new Uint8ClampedArray(layout.keys.length * 3);
    canvas.setAttribute("width", Math.round(layout.width + pad * 2));
    canvas.setAttribute("height", Math.round(layout.height + pad * 2));
    this.resize();
  }

  resize() {
    const css = this.canvas.clientWidth || this.layout.width;
    const ratio = Math.min(window.devicePixelRatio || 1, 2);
    const scale = (css * ratio) / (this.layout.width + this.pad * 2);
    const w = Math.round((this.layout.width + this.pad * 2) * scale), h = Math.round((this.layout.height + this.pad * 2) * scale);
    if (this.canvas.width !== w) this.canvas.width = w;
    if (this.canvas.height !== h) this.canvas.height = h;
    this.scale = scale;
  }

  draw(colours = this.colours) {
    const { ctx, scale: s, pad } = this, keys = this.layout.keys;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
    ctx.setTransform(s, 0, 0, s, pad * s, pad * s);
    const r = 6;

    if (this.glow) {
      // Light spilling around lit keys, drawn first and blurred so it sits under the caps.
      ctx.save();
      ctx.filter = `blur(${Math.max(6, 14 * s) / s}px)`;
      ctx.globalAlpha = 0.55;
      for (let i = 0; i < keys.length; i++) {
        const R = colours[i * 3], G = colours[i * 3 + 1], B = colours[i * 3 + 2];
        if (R + G + B < 24) continue;
        const k = keys[i];
        ctx.fillStyle = `rgb(${R},${G},${B})`;
        ctx.fillRect(k.x - 4, k.y - 4, k.w + 8, k.h + 8);
      }
      ctx.restore();
    }

    for (let i = 0; i < keys.length; i++) {
      const k = keys[i];
      const R = colours[i * 3], G = colours[i * 3 + 1], B = colours[i * 3 + 2];
      const lit = R + G + B > 0;
      ctx.beginPath();
      ctx.roundRect(k.x, k.y, k.w, k.h, r);
      // A dimly lit key never looks darker than an unlit one (#1b2130).
      ctx.fillStyle = lit ? `rgb(${Math.max(R, 27)},${Math.max(G, 33)},${Math.max(B, 48)})` : "#1b2130";
      ctx.fill();
      if (!lit) { ctx.strokeStyle = "rgba(232,236,245,0.07)"; ctx.lineWidth = 1 / s; ctx.stroke(); }
      if (this.labels && k.w * s > 18) {
        const text = CAPS[k.label] ?? k.label;
        const bright = 0.2126 * R + 0.7152 * G + 0.0722 * B;
        ctx.fillStyle = !lit ? "rgba(140,149,168,0.7)" : bright > 140 ? "rgba(11,11,30,0.62)" : "rgba(255,255,255,0.78)";
        ctx.font = `600 ${k.h < 40 ? 11 : text.length > 3 ? 12 : 15}px Inter,"Segoe UI",system-ui,sans-serif`;
        ctx.textAlign = "center"; ctx.textBaseline = "middle";
        ctx.fillText(text, k.x + k.w / 2, k.y + k.h / 2 + 1, k.w - 8);
      }
    }
  }
}

/**
 * One clock for every keyboard on the page. Each player draws only while it's on screen; small ones at 30 fps.
 * With reduced motion asked for, nothing plays until someone presses play.
 */
const players = new Set();
let running = false;
export const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");

function tick(now) {
  for (const p of players) {
    if (!p.visible || !p.playing || !p.compiled) continue;
    if (now - p.drawnAt < p.interval) continue;
    p.drawnAt = now;
    render(p.compiled, (now - p.startedAt) / 1000 * p.speed, p.view.colours);
    p.view.draw();
  }
  running = players.size > 0;
  if (running) requestAnimationFrame(tick);
}

const watcher = "IntersectionObserver" in window
  ? new IntersectionObserver(entries => { for (const e of entries) e.target._player && (e.target._player.visible = e.isIntersecting); }, { rootMargin: "120px" })
  : null;

export class Player {
  constructor(view, { fps = 60, autoplay = true } = {}) {
    this.view = view; this.interval = 1000 / fps - 2; this.drawnAt = 0; this.speed = 1;
    this.visible = !watcher; this.playing = autoplay && !reducedMotion.matches; this.compiled = null;
    view.canvas._player = this;
    watcher?.observe(view.canvas);
    players.add(this);
    if (!running) { running = true; requestAnimationFrame(tick); }
    addEventListener("resize", () => { view.resize(); this.still(); });
  }

  load(compiled) {
    this.compiled = compiled;
    this.startedAt = performance.now();
    this.still();
  }

  /** Shows the first frame as painted, for a keyboard that isn't playing. */
  still() {
    if (!this.compiled) return;
    if (this.compiled.frames.length) this.view.colours.set(this.compiled.frames[0]);
    this.view.draw();
  }

  play() { this.playing = true; this.startedAt = performance.now(); this.drawnAt = 0; }
  stop() { this.playing = false; this.still(); }
}

export function secondsText(seconds) {
  if (seconds >= 60) return `${Math.floor(seconds / 60)} min ${Math.round(seconds % 60)} s`;
  return `${seconds < 10 ? seconds.toFixed(1).replace(/\.0$/, "") : Math.round(seconds)} s`;
}

export const MUSIC = {
  beat: "Steps on the beat",
  speed: "Faster when louder",
  pulse: "Pulses with the bass",
};
