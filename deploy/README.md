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
- `test.soovioo.cloud` a `<slug>.test.soovioo.cloud` — [testovací instance](#testovací-instance)
  s posledním commitem `main`. Slug `test` je proto pro školy vyhrazený.

Web volá API na stejném originu (`https://skola.soovioo.cloud/api/...`), takže žádné CORS.

### Weby dalších stacků

Porty 80 a 443 má na VPS jen Caddy tohoto stacku, takže TLS ukončuje i pro další stacky na
stejném serveru, které nejsou v tomhle repu. Každý z nich si při deployi uloží vlastní
konfiguraci webu do `/opt/soovioo/sites/<název>.caddy` (v kontejneru `/etc/caddy/sites`,
`Caddyfile` je importuje) a Caddy znovu načte:

```bash
docker exec soovioo-web-1 caddy reload --config /etc/caddy/Caddyfile
```

Adresář zakládá `deploy.sh`, jeho obsah spravují ty stacky a v tomhle repu není.

## Jak se nasazuje

„Postav jednou, pak povyš“ (#393):

1. Push do `main`, workflow **CI** (`.github/workflows/ci.yml`): job `images` souběžně s
   testy postaví image `api` a `web` (`ghcr.io/fidlip/soovioo-*:<sha>`). Až projdou všechny
   testy, nasadí je na [testovací instanci](#testovací-instance) (`deploy.yml`). Na produkci
   se z `main` nic nenasazuje.
2. Vydání: `scripts/release.sh [-v RR.M.N] [-c <commit>] [-n]` založí **koncept vydání**
   (draft release) v tomto repu i v backoffice se stejnou verzí. Bez `-c` vezme commit, který
   běží na testu, bez `-v` další pořadí v měsíci (`26.11.0`, `26.11.1`, …). Poznámky napíše
   česky Claude Code (`claude -p`) z PR od posledního vydání; `-n` je jen vypíše.
3. Poznámky upravit na GitHubu a **Publish**. Workflow **Release** (`release.yml`) ověří, že
   commit je v `main` a jeho CI prošlo, přidá image tagy `RR.M.N` a `latest` a nasadí je na
   produkci (`deploy.yml`: zkopíruje `docker-compose.yml`, `Caddyfile` a `deploy.sh` do
   `/opt/soovioo` a spustí `deploy.sh`). Kontrola: `version.json` ukazuje commit i verzi.
   Environment `production` pouští jen tagy `v*`.

Verze: produkce hlásí vydání (`APP_VERSION` v `.env`: API `App__Version`, web `version.json`
přes šablonu v Caddy), test datum commitu a jeho hash (`2026.11.08-13e982d`). Job `version`
(`.github/scripts/version-json.sh`) zapíše do `version.json` v image `web` i poslední PR,
které commit obsahuje. Admin a superadmin verzi vidí dole v sidebaru.

Na serveru se nic nebuilduje a není tam ani git checkout — jen `/opt/soovioo` s těmi
třemi soubory a `.env`. Migrace DB se aplikují samy při startu `api`.

Návrat na starší vydání: znovu spustit workflow Release jeho vydání (Actions → Re-run), nebo
na serveru `cd /opt/soovioo && APP_VERSION=26.11.0 sh deploy.sh <jeho commit>` (image z
posledního týdne zůstávají na serveru, starší po `docker login ghcr.io`).

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

Druhá instance aplikace na stejném VPS s posledním commitem `main`: samostatný compose projekt
`soovioo-test` v `/opt/soovioo-test` (soubory z `deploy/test/`) s vlastním Postgresem, API,
přílohami a [Mailpitem](https://mailpit.axllent.org/). Data jen ze seedu a dem — nikdy kopie
produkce, a bez záloh.

- Každý push do `main` se po testech nasadí do `/opt/soovioo-test` s kontrolou, že
  `https://test.soovioo.cloud/version.json` ukazuje commit. Na produkci jde až ve vydání.
- Žádný port nepublikuje. TLS ukončuje produkční Caddy (blok `test.{$MAIN_DOMAIN}` v
  `Caddyfile`, vlastní wildcard certifikát `*.test.soovioo.cloud` přes DNS-01 stejným tokenem)
  a posílá dál na `web` testu, který obsluhuje aplikaci a `/api` v čistém HTTP. Spojuje je
  interní síť `soovioo-test-proxy`, kterou zakládá produkční stack a kde jsou kromě něj jen služby
  testu, které obsluhuje — test nevidí produkční API ani databázi.
- DNS: `test.soovioo.cloud` i `<slug>.test.soovioo.cloud` pokrývají stávající záznamy
  `*.soovioo.cloud` (DNS wildcard platí pro libovolnou hloubku, když `test` nemá vlastní záznam).
- API s `App__MainDomain=test.soovioo.cloud`, `ASPNETCORE_ENVIRONMENT=Production`, bez demo
  účtů (jen `superadmin`), push (FCM) vypnutý. E-maily jdou do Mailpitu:
  `https://test.soovioo.cloud/mailpit/`, přihlášení z `MAILPIT_UI_AUTH`.
- Napojení na backoffice dává `BACKOFFICE_*` v `/opt/soovioo-test/.env` (prázdné = nepřipojeno).
- Paměťové limity (API 512 MB, Postgres 256 MB, web a Mailpit 128 MB), aby test nevyhladověl
  produkci.

Příprava (jednou, jako root; produkční stack už musí běžet s `Caddyfile` a sítí z tohoto repa):

```bash
mkdir -p /opt/soovioo-test && chown deploy:deploy /opt/soovioo-test
# šablona deploy/test/.env.example, vlastní tajné hodnoty (openssl rand -hex 32)
nano /opt/soovioo-test/.env && chown deploy:deploy /opt/soovioo-test/.env && chmod 600 /opt/soovioo-test/.env
```

Pak push do `main`. Logy: `cd /opt/soovioo-test && docker compose logs -f api web`; zrušení
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
