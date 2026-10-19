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
- `<slug>.soovioo.cloud` — škola. Certifikát se vydá **při první návštěvě** (Caddy
  on-demand TLS); backend (`/api/tls/ask`) ho povolí jen pro slug existující školy.
  Wildcard certifikát ani API DNS providera tedy nejsou potřeba.
- `soovioo.cz` — veřejný web, běží zvlášť na hostingu Forpsi; tento stack se ho netýká.

Web volá API na stejném originu (`https://demo.soovioo.cloud/api/...`), takže žádné CORS.

## Jak se nasazuje

Automaticky po pushi do `main`, ve workflow **CI** (`.github/workflows/ci.yml`):
job `images` souběžně s testy postaví image `api` a `web` a nahraje je do GHCR
(`ghcr.io/fidlip/soovioo-*`, tagy `latest` a SHA commitu). Až projdou všechny testy, job
`deploy` zkopíruje na server `docker-compose.yml`, `Caddyfile` a `deploy.sh` do
`/opt/soovioo` a spustí `deploy.sh` (pull + restart). Ručně: Actions → CI → Run workflow.

Na serveru se nic nebuilduje a není tam ani git checkout — jen `/opt/soovioo` s těmi
třemi soubory a `.env`. Migrace DB se aplikují samy při startu `api`.

Návrat na starší verzi (image z posledního týdne zůstávají na serveru):
`cd /opt/soovioo && sh deploy.sh <sha starého commitu>`.

## Požadavky na VPS

- Ubuntu 24.04 (nebo jiný Linux s Dockerem), 1–2 GB RAM stačí (build běží na GitHubu),
  ~20 GB disk.
- Otevřené porty 22, 80, 443 (TCP) a 443/UDP (HTTP/3).

## DNS

U Forpsi, jen pro `soovioo.cloud` (A → IPv4 VPS; AAAA až po zapnutí IPv6 v Dockeru):

| Záznam             | Typ   |
| ------------------ | ----- |
| `soovioo.cloud`    | A     |
| `*.soovioo.cloud`  | A     |

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
v `appsettings.Development.json`). Demo škola `demo` s ukázkovými daty ale vzniká
z migrací i tady — bez účtů, smazat nebo použít jako ukázku.

Logy: `cd /opt/soovioo && docker compose logs -f api web`.

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
