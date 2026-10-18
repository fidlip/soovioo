# Soovioo — frontend

Nx monorepo s webovou (Angular 22) a mobilní (NativeScript + Angular) aplikací a sdílenou
TS knihovnou. Zpět na [hlavní README](../README.md).

## Projekty

| Projekt                      | Cesta                      | Popis                                             |
| ---------------------------- | -------------------------- | ------------------------------------------------- |
| `web`                        | `apps/web`                 | webová aplikace, Optimus UI + Tailwind            |
| `web-e2e`                    | `apps/web-e2e`             | Playwright e2e testy webu                         |
| `nativescript-mobile`        | `apps/nativescript-mobile` | mobilní aplikace (Android/iOS)                    |
| `shared` (`@soovioo/shared`) | `libs/shared`              | API klienti a modely pro web i mobil, po agendách |

Webové stránky jsou v `apps/web/src/app/<agenda>-page/`, mobilní v
`apps/nativescript-mobile/src/features/`.

"Optimus UI" = `@openng/optimus-ui` — rebrand PrimeNG v20, API je 1:1 stejné jako PrimeNG
(`p-table`, `pButton`, `p-select`…).

## Vývoj

```bash
npm ci
npx nx serve web --host 0.0.0.0     # http://localhost:4200, API očekává na :5080
```

Adresa API se odvozuje z hostname stránky + port `5080`, takže web otevřený z mobilu v LAN
(`http://<IP>:4200`) volá API na `http://<IP>:5080`.

Škola se vybírá cestou (`/<slug>/…`), na hlavní doméně subdoménou (`<slug>.soovioo.cz`).

### Mobil

Potřebuje Android SDK / emulátor (případně Xcode pro iOS) a NativeScript CLI.

```bash
npx nx run nativescript-mobile:debug --configuration=prod   # bez --configuration zamrzne na interaktivním promptu
```

## Kontroly

```bash
npx nx format:check
npx nx run-many -t lint test typecheck
npx nx run-many -t build --exclude=nativescript-mobile
```

### E2E

```bash
npx playwright install chromium     # jednorázově
npx nx run web-e2e:e2e              # potřebuje běžící API i web dev server
```
