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
ROADMAP.md              technický stav, hotové a plánované věci
IDEAS.md                nápady k projednání
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
| `admin`      | administrátor demo školy                      |
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
