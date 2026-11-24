// The home page's big keyboard: plays the shared animations one after another.
import { loadLayout, compile, KeyboardCanvas, Player, reducedMotion } from "./keyboard.js";

const SHOW_SECONDS = 9;

async function start() {
  const canvas = document.getElementById("hero-keyboard");
  const caption = document.getElementById("now-playing");
  const dots = document.getElementById("hero-dots");
  const [layout, index] = await Promise.all([
    loadLayout(),
    fetch("animations/index.json").then(r => r.json()),
  ]);
  const view = new KeyboardCanvas(canvas, layout, { labels: true, glow: true, pad: 10 });
  const player = new Player(view, { fps: 60, autoplay: true });
  view.draw();

  // The eight newest, opening on the rainbow: it shows the most of what the keyboard can do.
  const newest = [...index].reverse();
  const first = newest.findIndex(a => a.file === "rainbow-drift.json");
  if (first > 0) newest.unshift(...newest.splice(first, 1));
  const list = newest.slice(0, 8);
  const cache = new Map();
  let current = -1, timer = 0;

  async function show(i) {
    current = (i + list.length) % list.length;
    const entry = list[current];
    if (!cache.has(entry.file)) cache.set(entry.file, fetch(`animations/${entry.file}`).then(r => r.json()).then(a => compile(a, layout)));
    player.load(await cache.get(entry.file));
    if (!reducedMotion.matches) player.play();

    caption.textContent = "";
    caption.append("Playing ");
    const link = document.createElement("a");
    link.href = `animations.html#${entry.file.replace(/\.json$/, "")}`;
    link.textContent = entry.name;
    caption.append(link, ` by ${entry.author}`);
    for (const [n, dot] of [...dots.children].entries()) dot.setAttribute("aria-current", String(n === current));
    clearTimeout(timer);
    if (!reducedMotion.matches) timer = setTimeout(() => show(current + 1), SHOW_SECONDS * 1000);
  }

  for (const [n, entry] of list.entries()) {
    const dot = document.createElement("button");
    dot.type = "button";
    dot.setAttribute("aria-label", `Play ${entry.name}`);
    dot.addEventListener("click", () => show(n));
    dots.append(dot);
  }
  if (list.length) show(0);
}

start().catch(() => { /* The page reads fine without the keyboard. */ });

// The demo film loads and plays only while it's in sight, and only by hand when the visitor asked for less motion.
const demo = document.getElementById("demo");
if (demo && "IntersectionObserver" in window) {
  new IntersectionObserver(([entry]) => {
    if (entry.isIntersecting && !reducedMotion.matches) demo.play().catch(() => {});
    else if (!entry.isIntersecting) demo.pause();
  }, { threshold: 0.4 }).observe(demo);
}
