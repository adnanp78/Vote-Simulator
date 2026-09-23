/* Service worker — BEZ keša.
   Uvijek traži svježu verziju sa servera (cache: "no-cache" zaobilazi i HTTP keš
   browsera; GitHub Pages inače daje max-age=600 pa bi stara verzija visila do 10 min).
   Pri aktivaciji briše sve stare keševe (glasanje-v1…v14) sa uređaja.
   SW i dalje postoji jer bez njega nema "Dodaj na početni ekran" i zaobilaženja HTTP keša. */

self.addEventListener("install", () => self.skipWaiting());

self.addEventListener("activate", event => {
  event.waitUntil(
    caches.keys()
      .then(keys => Promise.all(keys.map(k => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener("fetch", event => {
  const req = event.request;
  if (req.method !== "GET") return;
  const url = new URL(req.url);
  if (url.origin !== self.location.origin) return;   // CDN (QR biblioteka) ide normalno

  event.respondWith(fetch(req.url, { cache: "no-cache", credentials: "same-origin" }));
});
