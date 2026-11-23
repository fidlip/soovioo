# Nasazení (produkce)

Jeden VPS, všechno v Dockeru (`docker-compose.yml` v tomto adresáři, na serveru v `/opt/soovioo`):

| Služba      | Co dělá                                                                             |
| ----------- | ----------------------------------------------------------------------------------- |
| `web`       | Caddy: HTTPS (Let's Encrypt), statický Angular build, `/api/*` → `api`              |
| `api`       | .NET backend (port 8080 jen uvnitř Docker sítě), přílohy ve volume `attachments`    |
| `postgres`  | PostgreSQL 17, data ve volume `postgres-data`, ven nepublikovaný                    |
| `db-backup` | denní `pg_dump` do volume `backups` (7 denních, 4 týdenní, 6 měsíčních)              |

Domény:

- `soovioo.cloud` — aplikace bez školy (superadmin oblast).
- `<slug>.soovioo.cloud` — škola. Všechny školy pokrývá jeden **wildcard certifikát**
  `*.soovioo.cloud` (Let's Encrypt, vydává a obnovuje Caddy přes DNS-01 výzvu v zóně na
  Cloudflare — viz [DNS](#dns)). Nová škola tak funguje hned a nehrozí limit Let's Encrypt
  (50 nových certifikátů týdně na doménu). Neexistující slug dostane platný certifikát a
  SPA ukáže „škola neexistuje“.
- `soovioo.cz` — veřejný web, běží zvlášť na hostingu Forpsi; tento stack se ho netýká.
- `soovioo.online` (`BACKOFFICE_DOMAIN`) — administrace backoffice (vlastní stack v soukromém
  repu, připojený do sítě tohoto jako `backoffice`). Caddy pouští ven jen `/admin` a
  `/admin-api`, certifikát běžnou HTTP-01 výzvou. Záznamy `A`/`AAAA` domény na adresy VPS.
  Samostatná doména, aby administraci nesdílel „site“ s veřejným webem a obchodem pod `soovioo.cz`.
- `test.soovioo.cloud` a `<slug>.test.soovioo.cloud` — [testovací instance](#testovací-instance)
  z větve `test`. Slug `test` je proto pro školy vyhrazený. `test.soovioo.online` — její
  testovací backoffice.

Web volá API na stejném originu (`https://skola.soovioo.cloud/api/...`), takže žádné CORS.

## Jak se nasazuje

Automaticky po pushi do `main`, ve workflow **CI** (`.github/workflows/ci.yml`):
job `images` souběžně s testy postaví image `api` a `web` a nahraje je do GHCR
(`ghcr.io/fidlip/soovioo-*`, tagy `latest` a SHA commitu). Až projdou všechny testy, job
`deploy` zkopíruje na server `docker-compose.yml`, `Caddyfile` a `deploy.sh` do
`/opt/soovioo` a spustí `deploy.sh` (pull + restart). Ručně: Actions → CI → Run workflow.

Verze je datum commitu a jeho zkrácený hash (`2026.11.08-13e982d`). Job `version`
(`.github/scripts/version-json.sh`) ji spolu s posledními PR, které commit obsahuje,
zapíše do `version.json` v image `web`. Admin a superadmin ji vidí dole v sidebaru.

Na serveru se nic nebuilduje a není tam ani git checkout — jen `/opt/soovioo` s těmi
třemi soubory a `.env`. Migrace DB se aplikují samy při startu `api`.

Návrat na starší verzi (image z posledního týdne zůstávají na serveru):
`cd /opt/soovioo && sh deploy.sh <sha starého commitu>`.

## Požadavky na VPS

- Ubuntu 24.04 (nebo jiný Linux s Dockerem), 1–2 GB RAM stačí (build běží na GitHubu),
  ~20 GB disk.
- Otevřené porty 22, 80, 443 (TCP) a 443/UDP (HTTP/3).

## DNS

Doména `soovioo.cloud` je registrovaná u Forpsi, ale **zónu obsluhuje Cloudflare**
(nameservery `*.ns.cloudflare.com`, tarif Free) — Forpsi nemá DNS API, které Caddy pro
wildcard certifikát potřebuje. `soovioo.cz` zůstává celé u Forpsi.

Záznamy v Cloudflare, všechny **DNS only (šedý mráček), ne proxy** — TLS ukončuje Caddy a
audit log vidí skutečné IP klientů:

| Záznam             | Typ  | Hodnota                 |
| ------------------ | ---- | ----------------------- |
| `soovioo.cloud`    | A    | `85.255.10.227`         |
| `*.soovioo.cloud`  | A    | `85.255.10.227`         |
| `soovioo.cloud`    | AAAA | `2001:15e8:110:22e3::1` |
| `*.soovioo.cloud`  | AAAA | `2001:15e8:110:22e3::1` |

- **API token** pro Caddy: Cloudflare → My Profile → API Tokens, šablona *Edit zone DNS*,
  oprávnění `Zone · DNS · Edit` jen pro zónu `soovioo.cloud`, IP filtr na adresy VPS, bez
  expirace. Uložený jen v `/opt/soovioo/.env` jako `CLOUDFLARE_API_TOKEN` (do GitHub secrets
  nepatří — deploy ho nepotřebuje). Ověření z VPS:
  `curl -s -H "Authorization: Bearer <token>" https://api.cloudflare.com/client/v4/user/tokens/verify`.
  Caddy jím jen při vydání/obnově (~60 dní) založí a smaže TXT `_acme-challenge`.
- **DNSSEC** zapnuté v Cloudflare (DNS → Settings), DS záznam vložený u Forpsi. Při změně
  nameserverů nejdřív DNSSEC u Forpsi vypnout a počkat, až DS zmizí — jinak doména přestane
  ověřujícím resolverům existovat.

Docker síť stacku má zapnuté IPv6, takže audit log vidí skutečné IPv6 adresy klientů.
Pozor: když `docker compose up` síť kvůli změně jejího nastavení přetvoří, kontejnery
ztratí DNS aliasy služeb (`api`, `postgres`) a `api` padá na „Resource temporarily
unavailable“ — pak pomůže `docker compose down && docker compose up -d`.

## Jednorázová příprava serveru (jako root)

```bash
curl -fsSL https://get.docker.com | sh
ufw allow OpenSSH && ufw allow 80/tcp && ufw allow 443 && ufw enable

# uživatel, pod kterým se přihlašuje GitHub Actions
adduser --disabled-password --gecos "" deploy
usermod -aG docker deploy
mkdir -p /opt/soovioo && chown deploy:deploy /opt/soovioo
mkdir -p /home/deploy/.ssh && chmod 700 /home/deploy/.ssh
echo "<veřejný klíč deploy-klíče>" >> /home/deploy/.ssh/authorized_keys
chown -R deploy:deploy /home/deploy/.ssh && chmod 600 /home/deploy/.ssh/authorized_keys

# konfigurace (šablona: deploy/.env.example; tajné hodnoty: openssl rand -hex 32)
nano /opt/soovioo/.env && chown deploy:deploy /opt/soovioo/.env && chmod 600 /opt/soovioo/.env
```

Deploy-klíč je samostatný pár jen pro GitHub (`ssh-keygen -t ed25519 -N "" -f soovioo-deploy`),
ne osobní klíč. Členství ve skupině `docker` je v praxi root na serveru — klíč proto patří
jen do GitHub secrets.

## GitHub secrets

Settings → Secrets and variables → Actions:

| Secret            | Hodnota                                                            |
| ----------------- | ------------------------------------------------------------------ |
| `VPS_HOST`        | `85.255.10.227`                                                    |
| `VPS_USER`        | `deploy`                                                           |
| `VPS_SSH_KEY`     | soukromý deploy-klíč (obsah souboru `soovioo-deploy`)              |
| `VPS_KNOWN_HOSTS` | výstup `ssh-keyscan 85.255.10.227` (ověří, že se mluví se serverem) |

## První spuštění

Po prvním úspěšném Deploy: `https://soovioo.cloud` → přihlásit `superadmin` heslem
z `SUPERADMIN_PASSWORD` (vynutí změnu hesla). Demo účty (`admin`/`ucitel`/… s hesly
`…123`) se v produkci **nezakládají** (`Seed:DemoAccounts` je zapnuté jen
v `appsettings.Development.json`). Testovací škola `skola` (dříve `demo`) s ukázkovými daty ale vzniká
z migrací i tady — bez účtů, smazat nebo použít jako ukázku. Adresy `demo` a `demo-…`
patří ukázkovým školám „Vyzkoušet“: `https://demo.soovioo.cloud` je průvodce, každé demo
běží na `demo-<id>.soovioo.cloud` (wildcard certifikát ho pokrývá), denní Hangfire úloha
`demo-schools-daily` maže prošlá dema a posílá připomínky. Limity viz `Demo:*` v
[backend/README.md](../backend/README.md); tlačítko „Vyzkoušet“ na `soovioo.cz` vede na
`https://demo.soovioo.cloud`.

Logy: `cd /opt/soovioo && docker compose logs -f api web`.

## Testovací instance

Druhá instance aplikace na stejném VPS, nasazovaná z větve `test`: samostatný compose projekt
`soovioo-test` v `/opt/soovioo-test` (soubory z `deploy/test/`) s vlastním Postgresem, API,
přílohami a [Mailpitem](https://mailpit.axllent.org/). Data jen ze seedu a dem — nikdy kopie
produkce, a bez záloh.

- Push do `test` projde stejným CI jako `main`: testy, image (tagy `test` a SHA commitu), deploy
  do `/opt/soovioo-test` a kontrola, že `https://test.soovioo.cloud/version.json` ukazuje commit.
  Do `test` se jde jen přes PR (rebase merge), do produkce PR z `test` do `main`; po deployi
  produkce CI srovná `test` na `main` (job `sync-test`) — viz [CONTRIBUTING.md](../CONTRIBUTING.md#pull-requests).
- Žádný port nepublikuje. TLS ukončuje produkční Caddy (blok `test.{$MAIN_DOMAIN}` v
  `Caddyfile`, vlastní wildcard certifikát `*.test.soovioo.cloud` přes DNS-01 stejným tokenem)
  a posílá dál na `web` testu, který obsluhuje aplikaci a `/api` v čistém HTTP. Spojuje je
  interní síť `soovioo-test-proxy`, kterou zakládá produkční stack a kde je kromě něj jen `web`
  testu a testovací backoffice — test nevidí produkční API, databázi ani backoffice.
- DNS: `test.soovioo.cloud` i `<slug>.test.soovioo.cloud` pokrývají stávající záznamy
  `*.soovioo.cloud` (DNS wildcard platí pro libovolnou hloubku, když `test` nemá vlastní záznam).
- API s `App__MainDomain=test.soovioo.cloud`, `ASPNETCORE_ENVIRONMENT=Production`, bez demo
  účtů (jen `superadmin`), push (FCM) vypnutý. E-maily jdou do Mailpitu:
  `https://test.soovioo.cloud/mailpit/`, přihlášení z `MAILPIT_UI_AUTH`.
- Testovací backoffice (vlastní stack z repa backoffice, `/opt/soovioo-backoffice-test`) je
  v síti testu (`soovioo-test_default`) jako `backoffice`; připojení dává `BACKOFFICE_*` v
  `/opt/soovioo-test/.env` (prázdné = nepřipojeno). Administrace na `test.soovioo.online`
  (blok `test.{$BACKOFFICE_DOMAIN}` v `Caddyfile`, HTTP-01; DNS `A`/`AAAA` u Forpsi).
- Paměťové limity (API 512 MB, Postgres 256 MB, web a Mailpit 128 MB), aby test nevyhladověl
  produkci.

Příprava (jednou, jako root; produkční stack už musí běžet s `Caddyfile` a sítí z tohoto repa):

```bash
mkdir -p /opt/soovioo-test && chown deploy:deploy /opt/soovioo-test
# šablona deploy/test/.env.example, vlastní tajné hodnoty (openssl rand -hex 32)
nano /opt/soovioo-test/.env && chown deploy:deploy /opt/soovioo-test/.env && chmod 600 /opt/soovioo-test/.env
```

Pak push do `test`. Logy: `cd /opt/soovioo-test && docker compose logs -f api web`; zrušení
i s daty: `docker compose down -v`.

## Zálohy

```bash
docker compose exec db-backup /backup.sh                 # záloha hned
docker compose exec db-backup ls -R /backups             # co existuje
# obnova (zahodí současnou DB!):
docker compose stop api
docker compose exec postgres sh -c 'dropdb -U soovioo soovioo && createdb -U soovioo soovioo'
docker compose exec -T db-backup sh -c 'zcat /backups/daily/soovioo-latest.sql.gz' \
  | docker compose exec -T postgres psql -U soovioo -d soovioo
docker compose start api
```

Záloha obsahuje schéma `public` (všechna data aplikace); schéma `hangfire` (fronta
úloh) se po obnově založí samo a opakované úlohy se zaregistrují při startu `api`.

Zálohy zatím leží **na stejném disku** a nezahrnují přílohy (volume `attachments`) —
mimo server je zatím nekopíruje nic (TODO: off-site, např. snapshoty u providera
nebo rclone do úložiště).

## Lokální zkouška

Na vlastním stroji bez veřejných domén: do kopie `Caddyfile` přidat do globálního
bloku `local_certs`, namapovat porty a zkoušet přes
`curl -k --resolve demo.soovioo.cloud:18443:127.0.0.1 https://demo.soovioo.cloud:18443/`.
