# Soovioo

Školní informační systém. Evidence žáků a tříd, známky, docházka,
rozvrh, zprávy, kalendář akcí, domácí úkoly, chování, platby, export do matriky
MŠMT. Jedna instalace obsluhuje více škol (multi-tenancy, škola podle slugu v URL nebo
subdomény).

## Stack

| Vrstva  | Technologie                                                                 |
| ------- | --------------------------------------------------------------------------- |
| Web     | Angular 22, Optimus UI (`@openng/optimus-ui`, API = PrimeNG), Tailwind      |
| Mobil   | NativeScript + Angular (Android, iOS)                                       |
| Sdílené | TS knihovna `frontend/libs/shared` (API klienti, modely) v Nx monorepu      |
| Backend | .NET 10 / ASP.NET Core, EF Core, Hangfire (plánované úlohy)                 |
| Data    | PostgreSQL 17, Mailpit (lokální zachytávač e-mailů v dev)                   |

## Struktura repa

```
backend/                .NET řešení (viz backend/README.md)
  src/Soovioo.Api/      API — jedna složka na agendu (Grades/, Attendance/, Messaging/…)
  tests/Soovioo.Api.Tests/  xUnit testy
  docker-compose.yml    PostgreSQL + Mailpit pro lokální vývoj
frontend/               Nx workspace (viz frontend/README.md)
  apps/web/             Angular webová aplikace
  apps/web-e2e/         Playwright e2e testy
  apps/nativescript-mobile/  mobilní aplikace
  libs/shared/          sdílený TS kód (web + mobil)
.github/workflows/ci.yml  CI — frontend, backend, e2e
LICENSE                 EUPL-1.2 (viz sekce Licence)
CONTRIBUTING.md, CLA.md  jak přispět, licenční ujednání pro přispěvatele
SECURITY.md             hlášení zranitelností
```

## Rychlé spuštění (dev)

Předpoklady: Docker, .NET 10 SDK, Node 24.

```bash
# 1. Databáze + Mailpit
docker compose -f backend/docker-compose.yml up -d

# 2. API (jednorázově nastavit podpisový klíč JWT, pak spustit)
cd backend/src/Soovioo.Api
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)"
dotnet run --urls http://0.0.0.0:5080

# 3. Web (v jiném terminálu)
cd frontend
npm ci
npx nx serve web --host 0.0.0.0
```

Web běží na <http://localhost:4200>, API na <http://localhost:5080>. Migrace a demo data se
aplikují automaticky při startu API.

Demo účty (heslo = jméno + `123`, např. `admin` / `admin123`):

| Účet         | Role                                          |
| ------------ | --------------------------------------------- |
| `superadmin` | správa škol (bez vazby na školu)              |
| `admin`      | administrátor testovací školy (`/skola`)     |
| `ucitel`     | učitel, třídní 5.A                            |
| `rodic`      | rodič žáka Jan Novák                          |
| `zak`        | žák Jan Novák                                 |

Pozvánkové a další e-maily v dev skončí v Mailpitu: <http://localhost:8025>.

## Testy

```bash
cd backend && dotnet test                         # backend
cd frontend && npx nx run-many -t lint test typecheck   # frontend
cd frontend && npx nx run web-e2e:e2e             # e2e (potřebuje běžící API i web)
```

Podrobnosti v [backend/README.md](backend/README.md) a [frontend/README.md](frontend/README.md).

## Plánování

Chyby, nápady a úkoly se vedou v [GitHub Issues](https://github.com/fidlip/soovioo/issues)
(nápady k projednání mají štítek `needs-discussion`), pořadí a stav práce v projektu
**Soovioo** na GitHubu. Každá změna jde přes pull request do `main`, který odkazuje
na svůj issue (`Closes #123`).

## Licence

Copyright 2026 Filip Šalda (<https://soovioo.cz>). Licensed under the EUPL.

Soovioo je open source pod licencí [EUPL-1.2](LICENSE) (European Union Public Licence),
kterou vydává Evropská komise. Licence má oficiální znění ve všech jazycích EU včetně
[češtiny](https://interoperable-europe.ec.europa.eu/collection/eupl/eupl-text-eupl-12)
a všechna jsou si rovnocenná.

Shrnutí (závazný je text licence):

- **Smíš**: Soovioo používat k jakémukoli účelu (např. si ho škola nasadí na vlastním
  serveru), studovat, upravovat a šířit, i upravené.
- **Musíš**: zachovat upozornění na autorská práva a licenci. Když upravené Soovioo šíříš
  nebo ho poskytuješ jiným přes síť (např. jako službu), musíš zpřístupnit zdrojový kód
  svých úprav pod EUPL-1.2 (nebo pod licencí, kterou EUPL uvádí jako kompatibilní).
- Licence nedává právo používat název a logo Soovioo.

Verze před přechodem na EUPL-1.2 (commity v historii repozitáře před touto změnou) byly
vydány pod licencí PolyForm Shield 1.0.0 a ta pro ně dál platí.

Závislosti mají vlastní licence. Upozornění: knihovna [QuestPDF](https://www.questpdf.com/license/)
(generování PDF) má vlastní licenční podmínky a bezplatná varianta neplatí pro každého.
Kdo Soovioo provozuje, ať si ověří, zda se na něj vztahuje.

Hosting, instalaci, podporu a zakázkový vývoj nabízí autor na <https://soovioo.cz>, kontakt
<info@soovioo.cz>. Jak přispět (včetně CLA): [CONTRIBUTING.md](CONTRIBUTING.md).
Bezpečnostní chyby: [SECURITY.md](SECURITY.md).

### In English

Soovioo is a school information system for Czech schools, so this README is in Czech. It is
open source under the [European Union Public Licence v. 1.2](LICENSE) (EUPL-1.2). You may
use, study, modify and redistribute it, for example to run it for your own school. If you
distribute a modified version or make it available to others over a network, you must
publish the source code of your changes under the EUPL-1.2 or a compatible licence. The
licence does not cover the Soovioo name and logo. Versions released before the switch to
the EUPL-1.2 remain under the PolyForm Shield 1.0.0 licence. Dependencies have their own
licences; note that QuestPDF has its own licence terms and its free tier does not apply to
everyone, so operators should check them. For contributing (including the CLA), see
[CONTRIBUTING.md](CONTRIBUTING.md). To report security issues, see [SECURITY.md](SECURITY.md).
