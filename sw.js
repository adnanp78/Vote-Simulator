/* Service worker — offline keširanje + omogućava "Dodaj na početni ekran" */
const CACHE = "glasanje-v3";

// App shell — keširamo odmah pri instalaciji
const SHELL = [
  "./",
  "./index.html",
  "./lista.html",
  "./qr.html",
  "./manifest.json",
  "./icon.svg",
  "./icon-maskable.svg"
];

self.addEventListener("install", event => {
  event.waitUntil(
    caches.open(CACHE)
      .then(c => c.addAll(SHELL))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener("activate", event => {
  event.waitUntil(
    caches.keys()
      .then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener("fetch", event => {
  const req = event.request;
  if (req.method !== "GET") return;

  const url = new URL(req.url);
  // samo isti origin (preskačemo CDN itd.)
  if (url.origin !== self.location.origin) return;

  const isHTML =
    req.mode === "navigate" ||
    req.destination === "document" ||
    url.pathname.endsWith(".html") ||
    url.pathname.endsWith("/");

  // HTML i JSON: network-first (uvijek najnovija verzija kad ima signala),
  // fallback na keš kad nema interneta — tako mobilni nikad ne zaglavi na staroj verziji.
  if (isHTML || url.pathname.endsWith(".json")) {
    event.respondWith(
      fetch(req)
        .then(resp => {
          const copy = resp.clone();
          caches.open(CACHE).then(c => c.put(req, copy));
          return resp;
        })
        .catch(() => caches.match(req).then(cached => cached || caches.match("./index.html")))
    );
    return;
  }

  // statički resursi (ikone, manifest): cache-first, pa mreža
  event.respondWith(
    caches.match(req).then(cached => cached || fetch(req).then(resp => {
      const copy = resp.clone();
      caches.open(CACHE).then(c => c.put(req, copy));
      return resp;
    }))
  );
});
