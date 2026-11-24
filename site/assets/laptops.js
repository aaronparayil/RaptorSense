// "Will it work on my laptop?": looks the model up in the lists on the page, which are the only copy of them.
const REPORT = "https://github.com/aaronparayil/RaptorSense/issues/new?template=laptop-report.yml";
const DOWNLOAD = "https://github.com/aaronparayil/RaptorSense/releases/latest";

const items = [...document.querySelectorAll("#models [data-model]")];
const models = new Map(items.map(li => [li.dataset.model, { li, state: li.closest("[data-state]").dataset.state, name: li.firstChild.textContent.trim() }]));
// People write these for the model on the right.
const ALIASES = { "PH16S-71": "PHN16S-71" };

const WORDS = {
  tested: ["Yes, it's tested", "RaptorSense was made and tested on this laptop."],
  found: ["It should light, but we haven't tried it", "Owners of this model report a keyboard chip that RaptorSense knows since 1.9. Close PredatorSense or NitroSense, start RaptorSense, and the Lighting tab should say it has 4 lighting zones. Fans and power stay read-only. Please tell us how it goes."],
  test: ["You can try it", "Install the helper on the Performance tab, then open the Keyboard test tab. It shows four colours and asks what you see. It changes only the keyboard's colours; fans and power stay read-only. Some of these have more than one kind of keyboard, so your report helps either way."],
  report: ["Not yet: we need a report", "Nobody has told us for sure which keyboard this model has. The laptop report asks for one PowerShell command that only reads the model, the BIOS version and the connected keyboard."],
};

/** The model code in what someone typed: "Predator PHN16-72-52PC" is PHN16-72. Null if there is none. */
export function modelIn(text) {
  const found = text.toUpperCase().replace(/[\u2010-\u2015]/g, "-").match(/\b([A-Z]{2,3})\s?(\d{2,3}S?)(?:\s*-\s*|\s+)(\d{2})(?!\d)/);
  if (!found) return null;
  const code = `${found[1]}${found[2]}-${found[3]}`;
  return ALIASES[code] ?? code;
}

/** The listed models that start with what someone typed: "PHN16" could be three of them. */
function startingWith(text) {
  const found = text.toUpperCase().match(/\b[A-Z]{2,3}\s?\d{2,3}S?\b/);
  return found ? [...models.keys()].filter(code => code.startsWith(`${found[0].replace(/\s/g, "")}-`)) : [];
}

function el(tag, className, text) {
  const e = document.createElement(tag);
  if (className) e.className = className;
  if (text) e.textContent = text;
  return e;
}

function link(text, href, primary) {
  const a = el("a", primary ? "btn primary" : "btn", text);
  a.href = href;
  return a;
}

function answer(title, text, buttons) {
  const result = document.getElementById("finder-result");
  const actions = el("div", "actions");
  actions.append(...buttons);
  result.replaceChildren(el("h4", "", title), el("p", "", text), actions);
}

function check(text) {
  for (const li of items) li.classList.remove("hit");
  const code = modelIn(text);
  const known = code && models.get(code);
  const report = link("Report your laptop", REPORT, !known || known.state === "report");
  if (known) {
    known.li.classList.add("hit");
    const [title, words] = WORDS[known.state];
    const name = `${known.name} ${code}`;
    answer(`${name}: ${title.charAt(0).toLowerCase()}${title.slice(1)}`, words,
      known.state === "report" ? [report] : known.state === "tested" ? [link("Download", DOWNLOAD, true)] : [link("Download", DOWNLOAD, true), report]);
  } else if (code) {
    answer(`${code}: we don't know yet`, "Per-key lighting may work if its keyboard is one of the known ones, and fans and power stay read-only. A report tells us which keyboard it has.", [report]);
  } else if (startingWith(text).length) {
    const some = startingWith(text);
    for (const code of some) models.get(code).li.classList.add("hit");
    answer("Which one is it?", `There is more than one: ${some.join(", ")}. The last two digits decide it.`, []);
  } else {
    answer("That doesn't look like a model code", "It looks like PH16-71, PHN16-72 or AN515-57: letters, a number, a dash and two more digits. RaptorSense is for Acer Predator and Nitro laptops.", []);
  }
}

const form = document.getElementById("finder");
if (form) {
  form.hidden = false;
  const input = document.getElementById("model");
  form.addEventListener("submit", e => {
    e.preventDefault();
    if (input.value.trim()) check(input.value);
    else input.focus();
  });
  input.addEventListener("input", () => {
    if (!input.value.trim()) {
      document.getElementById("finder-result").replaceChildren();
      for (const li of items) li.classList.remove("hit");
    }
  });
}
