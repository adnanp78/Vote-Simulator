# Vote Simulator 🗳️

Simulator glasačkog listića za izbore u Bosni i Hercegovini. Birač izabere svoju općinu i na telefonu — prije izbornog dana — vidi sve trke za koje glasa, pregleda kandidate i pripremi svoj izbor.

## Struktura

| Folder | Šta je |
|--------|--------|
| `public/` | **Javni sajt** — čisti HTML + JSON, ovo se objavljuje online |
| `public/data/` | Podaci: `index.json` (spisak općina), `<šifra>.json` po općini, `config.json` (javna adresa za QR) — **generiše admin, ne mijenjati ručno** |
| `admin/` | **Administracija** (.NET 10) — povlači podatke iz baze i generiše `public/data/` |
| root | Prezentacije i stari prototipovi listića; `index.html` preusmjerava na `public/` |

### `public/`

| Fajl | Opis |
|------|------|
| `index.html` | Izbor općine (pretraga) |
| `lista.html` | Simulator — `lista.html?kod=034` učitava `data/034.json`; na dnu dugme za A4 QR plakat |
| `pismo.js` | Latinica / ćirilica — izbor pri prvom ulasku, prekidač Lat/Ћир; podaci su na latinici, ćirilica se preslovljava u browseru |
| `manifest.json`, `sw.js`, `icon*.svg` | PWA ("Dodaj na početni ekran"). **Bez keša** — `sw.js` uvijek traži svježu verziju sa servera i briše stare keševe |

## Administracija — uvoz podataka

Izvor: view `Ombre_Kombinacije` u bazi `JIISdb` na `DEVENV-SQL2012\DEVELOPMENT` (Windows autentifikacija). Konekcija i ime view-a su u `admin/appsettings.json`.

```powershell
dotnet run --project admin
# otvori http://localhost:5080
```

- **Provjeri promjene** — povuče view i pokaže koje su se općine/trke promijenile, bez upisa.
- **Uvezi / ažuriraj podatke** — prepiše samo općine čiji su se podaci promijenili (poređenje po hashu), obriše općine kojih više nema, osvježi `index.json`.
- **Objavi (git push)** — commit + push samo `public/data/`.
- Lokalni pregled sajta sa svježim podacima: `http://localhost:5080/site/`

### Kako se view pretvara u listić

| LevelCode | Trka | Tip |
|-----------|------|-----|
| `701/702/703` | Predsjedništvo BiH (bošnjački / hrvatski / srpski član) | `predsjednistvo` |
| `51x` / `52x` | Zastupnički/Predstavnički dom PS BiH (FBiH / RS) | `otvorena-lista` |
| `600` | Predsjednik i potpredsjednici RS | `vecinski` |
| `4xx` | Zastupnički/Predstavnički dom Parlamenta FBiH | `otvorena-lista` |
| `3xx` | Narodna skupština RS | `otvorena-lista` |
| `2xx` | Skupština kantona | `otvorena-lista` |
| ostalo | naslov iz `CRName` (admin prikaže upozorenje) | `otvorena-lista` |

Stranke su poredane po `BallotOrder` i numerisane redom na listiću; kandidati po `ListPosition`.

## Pokretanje javnog sajta lokalno

Sajt koristi `fetch()` i Service Worker — treba HTTP server (ne radi preko `file://`). Najlakše kroz admin (`/site/`), ili:

```bash
cd public
python -m http.server 8080
```

---

> Odabiri birača ostaju samo na njegovom uređaju — ništa se ne šalje niti bilježi.
