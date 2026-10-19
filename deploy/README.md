# Nasazení (produkce)

Jeden VPS, všechno v Dockeru (`docker-compose.yml` v tomto adresáři):

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

## Požadavky na VPS

- Ubuntu 24.04 (nebo jiný Linux s Dockerem), **min. 2 GB RAM** — build frontendu
  (`npm ci` + Angular) je nejnáročnější krok; s 2 GB přidat swap (viz níže). ~20 GB disk.
- Otevřené porty 22, 80, 443 (TCP) a 443/UDP (HTTP/3).

## DNS

U Forpsi, jen pro `soovioo.cloud` (A → IPv4 VPS; AAAA až po zapnutí IPv6 v Dockeru):

| Záznam             | Typ   |
| ------------------ | ----- |
| `soovioo.cloud`    | A     |
| `*.soovioo.cloud`  | A     |

Nejdřív DNS, pak spustit stack — Caddy si certifikáty pro hlavní domény vyžádá hned
po startu.

## První spuštění

```bash
# Docker
curl -fsSL https://get.docker.com | sh

# swap (při 2 GB RAM)
fallocate -l 2G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
echo '/swapfile none swap sw 0 0' >> /etc/fstab

# firewall
ufw allow OpenSSH && ufw allow 80/tcp && ufw allow 443 && ufw enable

git clone <repo> /opt/soovioo && cd /opt/soovioo/deploy
cp .env.example .env    # vyplnit; tajné hodnoty: openssl rand -hex 32
docker compose up -d --build
docker compose logs -f api web
```

Pak `https://soovioo.cloud` → přihlásit `superadmin` heslem z `SUPERADMIN_PASSWORD`
(vynutí změnu hesla). Demo účty (`admin`/`ucitel`/… s hesly `…123`) se v produkci
**nezakládají** (`Seed:DemoAccounts` je zapnuté jen v `appsettings.Development.json`).
Demo škola `demo` s ukázkovými daty ale vzniká z migrací i tady — bez účtů, smazat
nebo použít jako ukázku.

## Aktualizace

```bash
cd /opt/soovioo && git pull && cd deploy && docker compose up -d --build
```

Migrace DB se aplikují samy při startu `api`. Starý build cache čas od času:
`docker builder prune -f`.

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
