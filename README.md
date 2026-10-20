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
LICENSE                 PolyForm Shield 1.0.0 (viz sekce Licence)
CONTRIBUTING.md, CLA.md  jak přispět, licenční ujednání pro přispěvatele
SECURITY.md             hlášení zranitelností
IDEAS.md, ROADMAP.md    lokální poznámky autora (v .gitignore, nejsou v repu)
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

## Licence

Zdrojový kód je veřejně dostupný pod licencí [PolyForm Shield 1.0.0](LICENSE). Není to
open source v užším smyslu (OSI), protože licence zakazuje konkurenční využití.

Shrnutí (závazné je jen anglické znění v [LICENSE](LICENSE)):

- **Smíš**: používat Soovioo pro vlastní potřebu (např. si ho škola nasadí na svém serveru),
  upravovat ho, forkovat a šířit, pokud zachováš text licence a řádek `Required Notice`.
- **Nesmíš**: poskytovat produkt nebo službu, která konkuruje Soovioo nebo službám, které
  autor nad Soovioo nabízí (hosting, instalace, podpora, zakázkový vývoj, školení). Platí
  to i pro služby poskytované zdarma.
- Pokud autor některou službu přestane poskytovat, smí ji dál poskytovat kdokoli (oddíl
  _Discontinued Products_). Školy tak nepřijdou o provoz, když autor skončí.

Hosting, instalaci, podporu a zakázkový vývoj nabízí autor na <https://soovioo.cz>, kontakt
<info@soovioo.cz>. Jak přispět (včetně CLA): [CONTRIBUTING.md](CONTRIBUTING.md).
Bezpečnostní chyby: [SECURITY.md](SECURITY.md).

### In English

Soovioo is a school information system for Czech schools, so this README is in Czech. The
source code is available under the [PolyForm Shield 1.0.0](LICENSE) license. You may use,
modify, fork and redistribute it, for example to run it for your own school. You may not
provide any product or service that competes with Soovioo or with the services its author
provides using it (hosting, installation, support, custom development, training). This is
source-available software, not OSI open source. For contributing (including the CLA), see
[CONTRIBUTING.md](CONTRIBUTING.md). To report security issues, see [SECURITY.md](SECURITY.md).
