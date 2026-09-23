/* =========================================================
   PISMO — latinica / ćirilica
   Podaci su na latinici; kad korisnik izabere ćirilicu, sav prikazani
   tekst se preslovljava u letu (i onaj koji se kasnije doda u DOM).
   Izbor se pamti na uređaju (localStorage "pismo").
   ========================================================= */
(function () {
  const KEY = "pismo";
  let mode = null;
  try { mode = localStorage.getItem(KEY); } catch (e) {}
  if (mode !== "lat" && mode !== "cyr") mode = null;
  // Dok pismo nije izabrano, stranice skrivaju .hide-unset i prikazuju .show-unset (neutralan ekran)
  if (!mode) document.documentElement.classList.add("pismo-unset");

  const LAT = "ABVGDĐEŽZIJKLMNOPRSTĆUFHCČŠabvgdđežzijklmnoprstćufhcčš";
  const CYR = "АБВГДЂЕЖЗИЈКЛМНОПРСТЋУФХЦЧШабвгдђежзијклмнопрстћуфхцчш";
  const L2C = {}, C2L = {};
  for (let i = 0; i < LAT.length; i++) { L2C[LAT[i]] = CYR[i]; C2L[CYR[i]] = LAT[i]; }
  const DI = { LJ: "Љ", Lj: "Љ", lj: "љ", NJ: "Њ", Nj: "Њ", nj: "њ", "DŽ": "Џ", "Dž": "Џ", "dž": "џ" };
  Object.assign(C2L, { "Љ": "Lj", "љ": "lj", "Њ": "Nj", "њ": "nj", "Џ": "Dž", "џ": "dž" });

  // Riječi sa q/w/x/y (QR, strana imena) ostaju latinicom — pola-pola izgleda gore
  function toCyr(s) {
    return String(s).replace(/[A-Za-zĐđŽžĆćČčŠš]+/g, w =>
      /[qwxyQWXY]/.test(w) ? w
        : w.replace(/LJ|Lj|lj|NJ|Nj|nj|DŽ|Dž|dž/g, m => DI[m]).replace(/./g, c => L2C[c] || c));
  }
  function toLat(s) { return String(s).replace(/[Ѐ-ӿ]/g, c => C2L[c] || c); }
  const t = s => mode === "cyr" ? toCyr(s) : s;

  /* ---- preslovljavanje DOM-a ---- */
  const SKIP = "script,style,code,pre,textarea,.no-translit";
  const ATTRS = ["placeholder", "title", "aria-label"];

  function convertText(node) {
    const p = node.parentElement;
    if (!p || p.closest(SKIP)) return;
    const v = node.nodeValue, c = toCyr(v);
    if (c !== v) node.nodeValue = c;
  }
  function convertEl(el) {
    if (el.closest(SKIP)) return;
    for (const a of ATTRS) {
      const v = el.getAttribute(a);
      if (v) { const c = toCyr(v); if (c !== v) el.setAttribute(a, c); }
    }
  }
  function convertTree(root) {
    if (root.nodeType === 3) return convertText(root);
    if (root.nodeType !== 1) return;
    convertEl(root);
    root.querySelectorAll("[placeholder],[title],[aria-label]").forEach(convertEl);
    const w = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
    for (let n = w.nextNode(); n; n = w.nextNode()) convertText(n);
  }

  if (mode === "cyr") {
    document.documentElement.lang = "sr-Cyrl";
    new MutationObserver(muts => {
      for (const m of muts) {
        if (m.type === "characterData") convertText(m.target);
        else if (m.type === "attributes") convertEl(m.target);
        else m.addedNodes.forEach(convertTree);
      }
    }).observe(document.documentElement,
      { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ATTRS });
    const fixTitle = () => { const c = toCyr(document.title); if (c !== document.title) document.title = c; };
    fixTitle();
    document.addEventListener("DOMContentLoaded", () => { convertTree(document.body); fixTitle(); });
    new MutationObserver(fixTitle).observe(document.querySelector("title") || document.head, { childList: true, characterData: true, subtree: true });
  }

  function set(p) {
    try { localStorage.setItem(KEY, p); } catch (e) {}
    location.reload();   // vraćanje na latinicu traži originalni tekst → najjednostavnije je ponovo učitati
  }

  /* ---- UI: prekidač i izbor pri prvom ulasku ---- */
  const css = `
    .pismo-toggle { display: inline-flex; border: 1px solid #d8dade; border-radius: 999px; overflow: hidden; background: #fff; }
    .pismo-toggle button { font: inherit; font-size: 12px; font-weight: 600; padding: 4px 11px; border: 0; background: transparent; color: #666; cursor: pointer; }
    .pismo-toggle button.on { background: #1f6feb; color: #fff; }
    .pismo-choose { text-align: center; margin: 8px 0 18px; }
    .pismo-choose p { font-size: 14px; color: #555; margin-bottom: 12px; }
    .pismo-choose .btns { display: flex; gap: 10px; justify-content: center; }
    .pismo-choose button {
      flex: 1; max-width: 220px; font: inherit; font-size: 17px; font-weight: 700; padding: 16px 12px;
      border: 1px solid #d8dade; border-radius: 14px; background: #fff; color: #1a1a1a; cursor: pointer;
      box-shadow: 0 2px 8px rgba(0,0,0,0.05);
    }
    .pismo-choose button:active { transform: scale(0.98); }
    .pismo-choose button small { display: block; font-size: 12px; font-weight: 500; color: #888; margin-top: 2px; }
    html.pismo-unset .hide-unset { display: none !important; }
    html:not(.pismo-unset) .show-unset { display: none !important; }
    @media print { .pismo-toggle, .pismo-choose { display: none !important; } }`;
  document.head.insertAdjacentHTML("beforeend", `<style>${css}</style>`);

  function toggleHTML() {
    const cur = mode || "lat";
    return `<div class="pismo-toggle no-translit" role="group" aria-label="Pismo">
      <button data-pismo="lat" class="${cur === "lat" ? "on" : ""}">Lat</button><button data-pismo="cyr" class="${cur === "cyr" ? "on" : ""}">Ћир</button></div>`;
  }
  function chooseHTML() {
    return `<div class="pismo-choose no-translit">
      <p>Izaberite pismo<br>Изаберите писмо</p>
      <div class="btns">
        <button data-pismo="lat">Latinica<small>abc</small></button>
        <button data-pismo="cyr">Ћирилица<small>абв</small></button>
      </div></div>`;
  }

  document.addEventListener("click", e => {
    const b = e.target.closest("[data-pismo]");
    if (!b) return;
    if (b.dataset.pismo === (mode || "lat") && mode) return;
    set(b.dataset.pismo);
  });

  window.Pismo = {
    get mode() { return mode || "lat"; },
    chosen: mode !== null,
    t, toCyr, toLat,
    // Stranica stavi <div data-pismo-toggle></div> / <div data-pismo-choose></div> gdje želi UI
    mount() {
      document.querySelectorAll("[data-pismo-toggle]").forEach(el => el.innerHTML = mode ? toggleHTML() : "");
      document.querySelectorAll("[data-pismo-choose]").forEach(el => el.innerHTML = mode ? "" : chooseHTML());
    }
  };
  document.addEventListener("DOMContentLoaded", () => window.Pismo.mount());
})();
