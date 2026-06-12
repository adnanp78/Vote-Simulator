# Vote Simulator 🗳️

Simulator glasačkog listića za izbore u Bosni i Hercegovini. Omogućava biraču da na svom telefonu — prije izbornog dana — pogleda kandidate, razumije pravila glasanja i pripremi svoj izbor.

## Šta nudi

- **Pravi prikaz listića** za opšte (FBiH i RS) i lokalne izbore
- **Tri tipa glasanja** sa automatskim pravilima:
  - *Predsjedništvo* — samo jedan kandidat ukupno
  - *Otvorena lista* — stranka i/ili do 3 kandidata
  - *Većinski* (načelnik / predsjednik RS) — jedan kandidat
- **"Moj izbor"** — pregled svih odabira u realnom vremenu, klik na trku skoči na nju
- **Podsjetnik** — odabir se pamti na uređaju, pa birač na biralištu vidi šta je planirao
- **PWA** — "Dodaj na početni ekran", radi i bez interneta, kao prava aplikacija

## Fajlovi

| Fajl | Opis |
|------|------|
| `index.html` | Početna stranica — meni biračkih mjesta |
| `lista.html` | Glavni simulator (učitava listu preko `?kod=`) |
| `qr.html` | Generator QR koda i plakata za biralište |
| `manifest.json`, `sw.js`, `icon*.svg` | PWA (install + offline) |
| `001-gen.json`, `003-gen.json`, `001-mun.json` | Podaci listića |
| `prezentacija-simulator.html` | Prezentacija projekta |

## Pokretanje

Aplikacija koristi `fetch()` i Service Worker — treba HTTP(S) server (ne radi preko `file://`):

```bash
python -m http.server 8080
# pa otvori http://localhost:8080/
```

Za testiranje na telefonu (PWA install + offline traže HTTPS): deploy na Netlify / GitHub Pages / Cloudflare Pages, ili tunel (`cloudflared` / `ngrok`).

## Primjeri

- `lista.html?kod=001-gen` — Velika Kladuša, opšti izbori
- `lista.html?kod=003-gen` — Republika Srpska, opšti izbori
- `lista.html?kod=001-mun` — Velika Kladuša, lokalni izbori

---

> Demonstracioni projekt. Odabiri birača ostaju samo na njegovom uređaju — ništa se ne šalje niti bilježi.
