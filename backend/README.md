# Soovioo — backend

ASP.NET Core (.NET 10) API nad PostgreSQL. Jeden projekt `src/Soovioo.Api`, testy v
`tests/Soovioo.Api.Tests`. Zpět na [hlavní README](../README.md).

## Struktura

Kód je rozdělený podle agend, každá složka obsahuje entity i controllery:

| Složka                 | Obsah                                                            |
| ---------------------- | ---------------------------------------------------------------- |
| `Auth/`                | uživatelé, role, JWT + refresh tokeny (httpOnly cookie), pozvánky |
| `Evidence/`            | školy, třídy, žáci, předměty, učebny, vazba rodič↔žák            |
| `Grades/`              | známky, vysvědčení (PDF přes QuestPDF)                           |
| `Attendance/`          | absence                                                          |
| `Timetable/`           | rozvrh, verze, přidělení výuky, generátor (OR-Tools)             |
| `Messaging/`           | zprávy a přílohy                                                 |
| `Events/`              | kalendář akcí, ICS odběr                                         |
| `Homework/`, `Behavior/`, `Payments/` | domácí úkoly, chování, platby                     |
| `Dashboard/`           | souhrn pro domovskou stránku                                     |
| `Matrika/`             | export do matriky MŠMT (skeleton)                                |
| `Reference/`           | číselníky (státy, okresy, obce) — seed z `Data/Seed/*.csv`       |
| `Demo/`                | ukázkové školy „Vyzkoušet“: generátor dat, výběr uživatele, mazání |
| `Data/AppDbContext.cs` | EF Core kontext, per-school query filtry (multi-tenancy)         |
| `Migrations/`          | EF Core migrace                                                  |

## Spuštění

```bash
# PostgreSQL (localhost:5432, DB soovioo, postgres/postgres) + Mailpit
docker compose up -d

cd src/Soovioo.Api

# Jednorázově: podpisový klíč JWT (32+ znaků). V repu záměrně není — bez něj
# API při startu spadne s jasnou chybou. Na serveru přes env Jwt__SigningKey.
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)"

# 0.0.0.0, aby šlo API volat i z mobilu v LAN
dotnet run --urls http://0.0.0.0:5080
```

Při startu se automaticky aplikují migrace, nasejdují demo účty (viz hlavní README) a
číselníky. Číselníky se seedují jen jednou (pokud je tabulka `Countries` prázdná) — změna CSV
se na existující DB neprojeví.

### Konfigurace

| Klíč                                | Význam                                                         |
| ----------------------------------- | -------------------------------------------------------------- |
| `ConnectionStrings:Default`         | připojení k PostgreSQL                                         |
| `Jwt:SigningKey`                    | podpisový klíč JWT (user-secrets / env), povinný               |
| `Smtp:*`                            | odesílání e-mailů; v dev Mailpit na `localhost:1025`           |
| `App:FrontendBaseUrl`, `App:PathBasedTenant`, `App:MainDomain` | tvar odkazů v e-mailech (pozvánky) |
| `AuthCookie:Secure`                 | `Secure` příznak refresh-token cookie (v dev `false`, HTTP)    |
| `Cors:AllowedOrigins`               | povolené originy mimo Development (v dev se povolí jakýkoli)   |
| `Demo:LifetimeDays`, `Demo:MaxActive` | životnost dema (30 dní), strop současně existujících dem (200) |
| `Demo:CreatePerIpPerDay`            | nová dema / resety za den z jedné IP (5; v dev 1000)           |
| `Demo:FeedbackEmail`                | kam chodí zpětná vazba z dem (`info@soovioo.cz`)               |

## Migrace

Nástroj: `dotnet tool install -g dotnet-ef` (verze 10).

```bash
cd src/Soovioo.Api
dotnet ef migrations add NazevMigrace
dotnet ef database update          # volitelné, API je aplikuje samo při startu
```

## Testy

```bash
dotnet test                        # z backend/
```

xUnit, EF Core InMemory provider; testy volají controllery přímo s falešným kontextem školy
(`FakeCurrentSchoolContext`) a e-mailem (`FakeEmailSender`).

## Plánované úlohy (Hangfire)

Hangfire používá stejnou PostgreSQL databázi. Úlohy se registrují při startu v
`Program.cs` (např. generování splátek opakovaných plateb, úklid refresh tokenů — denně);
jednorázově běží generátor rozvrhu (`TimetableGenerationJob`).
Dashboard: <http://localhost:5080/hangfire> (výchozí nastavení Hangfire ho pouští jen z
localhostu).

## E-maily (Mailpit)

V dev se všechny e-maily (pozvánky při prvním přihlášení) posílají na lokální Mailpit —
nic neodchází ven. Webové rozhraní: <http://localhost:8025>.

## Přílohy

Přílohy zpráv a domácích úkolů se ukládají na disk do `src/Soovioo.Api/AttachmentStorage/{schoolId}/`
(ignorováno gitem).
