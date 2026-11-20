// The animations page: a card for each shared animation, playing on its own small keyboard.
// Names, credits and descriptions come from people who shared them, so they only ever go in as text.
import { loadLayout, compile, KeyboardCanvas, Player, reducedMotion, secondsText, MUSIC } from "./keyboard.js";

const grid = document.getElementById("anims");
const search = document.getElementById("search");
const count = document.getElementById("count");
const empty = document.getElementById("empty");
let filter = "all", sort = "new";
const cards = [];

function el(tag, className, text) {
  const e = document.createElement(tag);
  if (className) e.className = className;
  if (text !== undefined) e.textContent = text;
  return e;
}

function card(entry, order, layout) {
  const slug = entry.file.replace(/\.json$/, "");
  const article = el("article", "anim");
  article.id = slug;

  const screen = el("div", "screen");
  const canvas = el("canvas");
  canvas.setAttribute("role", "img");
  canvas.setAttribute("aria-label", `${entry.name} playing on the keyboard`);
  const play = el("button", "play");
  play.type = "button";
  play.append(el("span", "", "Play"));
  play.setAttribute("aria-label", `Play ${entry.name}`);
  screen.append(canvas, play);

  const body = el("div", "body");
  body.append(el("h3", "", entry.name), el("p", "by", `by ${entry.author}`));
  if (entry.about) body.append(el("p", "about", entry.about));
  const tags = el("div", "tags");
  const length = el("span", "tag", "");
  tags.append(length, el("span", "tag", `${entry.frames} frame${entry.frames === 1 ? "" : "s"}`));
  if (entry.music) tags.append(el("span", "tag music", `♪ ${MUSIC[entry.music] ?? "Follows the music"}`));
  const foot = el("div", "foot");
  // Opens RaptorSense (1.8.9 and newer register this link), which fetches this file from the site and adds it.
  const add = el("a", "btn primary", "Add to RaptorSense");
  add.href = `raptorsense://add-animation/${slug}`;
  add.title = "Opens RaptorSense and adds this animation. Needs RaptorSense 1.8.9 or newer.";
  foot.append(add);
  const download = el("a", "btn", "Download");
  download.href = `animations/${entry.file}`;
  download.setAttribute("download", entry.file);
  const copy = el("button", "link-btn", "Copy link");
  copy.type = "button";
  copy.addEventListener("click", async () => {
    const url = new URL(`#${slug}`, location.href).href;
    try { await navigator.clipboard.writeText(url); copy.textContent = "Link copied"; }
    catch { location.hash = slug; copy.textContent = "Link in the address bar"; }
    setTimeout(() => (copy.textContent = "Copy link"), 2000);
  });
  foot.append(download, copy);
  body.append(tags, foot);
  article.append(screen, body);

  const view = new KeyboardCanvas(canvas, layout, { pad: 6 });
  const player = new Player(view, { fps: 30, autoplay: true });
  view.draw();
  fetch(`animations/${entry.file}`).then(r => r.json()).then(animation => {
    const compiled = compile(animation, layout);
    length.textContent = `${secondsText(compiled.length)} loop`;
    player.load(compiled);
    if (player.playing) player.play();
  }).catch(() => { length.textContent = "Couldn't load"; });

  // With reduced motion, a card plays only when asked, and stops when asked again.
  const toggle = () => {
    if (!reducedMotion.matches) return;
    if (player.playing) { player.stop(); article.classList.remove("playing"); }
    else { player.play(); article.classList.add("playing"); }
  };
  play.addEventListener("click", toggle);
  screen.addEventListener("click", e => { if (e.target !== play && !play.contains(e.target)) toggle(); });

  const words = `${entry.name} ${entry.author} ${entry.about ?? ""}`.toLowerCase();
  return { article, entry, order, words };
}

function update() {
  const q = search.value.trim().toLowerCase();
  const shown = cards
    .filter(c => (filter === "all" || (filter === "music" && c.entry.music)) && (!q || c.words.includes(q)))
    .sort((a, b) => sort === "name" ? a.entry.name.localeCompare(b.entry.name) : b.order - a.order);
  const visible = new Set(shown);
  for (const c of cards) c.article.hidden = !visible.has(c);
  for (const c of shown) grid.append(c.article);
  count.textContent = `${shown.length} of ${cards.length}`;
  empty.hidden = shown.length > 0;
}

function pressed(group, value) {
  for (const b of document.querySelectorAll(`[data-${group}]`)) b.setAttribute("aria-pressed", String(b.dataset[group] === value));
}

function showTarget() {
  for (const c of cards) c.article.classList.remove("target");
  const id = decodeURIComponent(location.hash.slice(1));
  const target = id ? document.getElementById(id) : null;
  if (target?.classList.contains("anim")) {
    target.hidden = false;
    target.classList.add("target");
    target.scrollIntoView({ block: "center", behavior: reducedMotion.matches ? "auto" : "smooth" });
  }
}

async function start() {
  if (reducedMotion.matches) document.body.classList.add("still");
  const [layout, index] = await Promise.all([loadLayout(), fetch("animations/index.json").then(r => r.json())]);
  for (const [order, entry] of index.entries()) cards.push(card(entry, order, layout));
  update();
  showTarget();
  addEventListener("hashchange", showTarget);
}

search.addEventListener("input", update);
for (const b of document.querySelectorAll("[data-filter]")) b.addEventListener("click", () => { filter = b.dataset.filter; pressed("filter", filter); update(); });
for (const b of document.querySelectorAll("[data-sort]")) b.addEventListener("click", () => { sort = b.dataset.sort; pressed("sort", sort); update(); });
document.getElementById("clear").addEventListener("click", () => { search.value = ""; filter = "all"; pressed("filter", "all"); update(); });

start().catch(() => {
  count.textContent = "";
  empty.hidden = false;
  empty.textContent = "The animations couldn't be loaded. Try again in a moment.";
});
