# Data Schema & Migration Plan

Status: **draft** (2026-09-28). Covers the new database, how the legacy ArangoDB data maps into it, the export
procedure, and the cutover. Legacy model details: [legacy-bot-analysis.md §4](legacy-bot-analysis.md#4-data-model-arangodb-database-villagerhaven).

## 1. Database choice

**PostgreSQL via EF Core** (`Npgsql.EntityFrameworkCore.PostgreSQL`). Decided 2026-09-28, because 3–4 more bots
will join this suite.

- **One Postgres server for the whole bot suite** on the VM, with **one database per bot**, each with its own login
  role (e.g. database `villager_bot`, role `villager_bot`). The bots stay isolated (separate schemas and migrations,
  no cross-bot access) while sharing one server to run, patch, and back up.
- EF Core migrations manage each bot's schema. The bot applies pending migrations on startup
  (`Database:MigrateOnStartup`, default true).
- **Runs in Docker** (`deploy/compose.yaml`, project name `vh-bots`): a `postgres:18-alpine` service on a private
  `bots` network with no published ports, plus one service per bot. `deploy/postgres/init/10-provision-bots.sh`
  creates each bot's role and database from `BOT_DATABASES` and `<NAME>_DB_PASSWORD`, and closes each database to other
  roles. It's idempotent and re-runnable when a bot is added. `deploy/compose.dev.yaml` publishes Postgres on
  `127.0.0.1:5432` for local development.
- Backups: a nightly `pg_dump` per database (`deploy/backup.sh`, custom format), optionally copied off the VPS
  (deployment.md, Phase 6), on top of OVH's included daily VPS backup.

Conventions:
- Discord IDs are `ulong` in code and stored as **`bigint`**. Npgsql has no native unsigned mapping, but snowflakes fit
  in signed 64-bit, so a shared EF Core value converter maps `ulong` ↔ `long`.
- Timestamps are `DateTimeOffset` in code and **`timestamptz`** in Postgres. Npgsql requires UTC offsets when writing.
- Enums are stored as text, for readable data.
- Table and column names: use Npgsql's snake_case naming convention (`EFCore.NamingConventions`), so hand-written SQL stays
  pleasant.

## 2. Schema

### `ActiveRequests`: one row per member with a request in the queue

| Column | Type | Notes |
|---|---|---|
| `UserId` | ulong **PK** | One active request per member (as today) |
| `VillagerKey` | text | Catalogue key, e.g. `AGENT_S` (legacy enum key, kept for continuity) |
| `QueuePosition` | long, **unique** | A static "ticket number" for FIFO order (replaces `pos` + `lastpos.json`). See below. |
| `SubmittedAt` | DateTimeOffset | |
| `IsAvailable` | bool | Defaults false on creation |
| `PulledAt` | DateTimeOffset? | Null = not pulled (replaces `status` UNACCEPTED/ACCEPTED) |
| `HunterId` | ulong? | Set when pulled |
| `ChannelId` | ulong?, unique when not null | The request channel; `/close` looks up by this |

Indexes: `(PulledAt, IsAvailable, QueuePosition)` for the queue, `VillagerKey`, and `ChannelId`.

**How `QueuePosition` works (a deli ticket).**
- When a request is created it takes the next value of the Postgres sequence **`queue_position`** as its ticket
  (the column's default is `nextval('queue_position')`, so allocation is atomic).
  The ticket **never changes** afterwards. Changing villager or toggling availability keeps it; leaving and re-requesting
  gets a new ticket at the back.
- Gaps (from people leaving or being pulled) are normal and harmless. The ticket only defines order.
- The **displayed** position is computed live: *1 + the number of requests with a lower ticket* in the relevant set.
  Example: tickets 101 (pulled), 104, 107 (unavailable), 110 (you). Overall position = 1 + 3 = **4th**. Available-queue
  position (unpulled and available only; you are counted regardless of your own status) = 1 + 1 (104) = **2nd**.

### `ArchivedRequests`: closed requests (history and stats)

| Column | Type | Notes |
|---|---|---|
| `Id` | long PK (autoincrement) | |
| `UserId` | ulong | |
| `VillagerKey` | text | |
| `SubmittedAt` | DateTimeOffset? | Nullable: very old legacy rows may lack it |
| `PulledAt` | DateTimeOffset? | |
| `ClosedAt` | DateTimeOffset | Legacy `archiveTimeStamp` |
| `HunterId` | ulong? | |
| `Outcome` | text enum | `Completed`, `Timeout`, `Removed` (member left the server) |

Indexes: `(UserId, ClosedAt)` for history, `(Outcome, ClosedAt)` for stats, and `VillagerKey`.

### `OverflowCategories`: bot-created overflow categories

| Column | Type | Notes |
|---|---|---|
| `CategoryId` | ulong PK | |
| `Number` | int, unique | For the "Villager Requests {n}" name |
| `CreatedAt` | DateTimeOffset | |

### `BotState`: small key/value settings

| Key | Value |
|---|---|
| `PanelChannelId`, `PanelMessageId` | The info-channel panel message the bot edits on refresh |

### Not stored in the database

- **Villager catalogue:** shipped with the app as a JSON resource generated once from the legacy `Villager.java` enum
  (key, display name, internal game ID, plus gender, species, personality, catchphrase for completeness). Villager groups
  (Sanrio) live in config.
- **Blocks, reservations, freeze:** features removed. Their legacy exports are kept as files for reference only.

### Changes vs legacy

- `status` (UNACCEPTED/ACCEPTED/REMOVED) on active requests becomes `PulledAt` null / not null. An active `REMOVED`
  state never persisted in practice (removal archives immediately).
- `lastpos.json` is **not needed**: positions are renumbered from submission time on import (§4), and
  the `queue_position` sequence is set so the next ticket is one past the last renumbered position.
- The archive is renamed and normalized, with the same information.

## 3. Legacy data export (run on the current GCP VM)

This only reads data. Nothing on the old server changes.

### 3.1 Connect to the VM

From your PC (with the Google Cloud CLI installed and logged in):

```bash
gcloud compute instances list                       # find the instance name and zone
gcloud compute ssh INSTANCE_NAME --zone ZONE
```

Or use the **SSH** button next to the VM in the Cloud Console (Compute Engine → VM instances).

### 3.2 Find how ArangoDB is running and the bot's config

```bash
# Is ArangoDB installed directly, or in Docker?
which arangoexport arangosh
sudo docker ps 2>/dev/null | grep -i arango

# The bot writes config.json and lastpos.json in its working directory. Find them:
sudo find / -name lastpos.json -not -path "*/proc/*" 2>/dev/null
```

In that directory, `config.json` has `arangoDetails.hostName`, `port`, and `password`. **Don't paste this file to
me or anywhere else**: it also holds the old bot token. The database user is `root` (the old bot never sets
one) and the database is `villagerhaven`.

### 3.3a Export: ArangoDB installed on the VM

Replace `HOST` with the `hostName` from config (use `127.0.0.1` if it's this VM) and `PORT` with the configured port (normally `8529`).
You'll be prompted for the password.

```bash
arangoexport \
  --server.endpoint tcp://HOST:PORT \
  --server.authentication true \
  --server.username root \
  --server.database villagerhaven \
  --collection requests \
  --collection archivedrequests \
  --collection blocks \
  --collection reservations \
  --type jsonl \
  --output-directory ~/vh-export \
  --overwrite true
```

### 3.3b Export: ArangoDB in Docker

The official `arangodb` image includes the client tools (`arangoexport`, `arangosh`), so the export runs **inside the
container**. The files are then copied out.

**1. Find the container:**
```bash
sudo docker ps --format 'table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}'
```
Note the name in the `NAMES` column of the row whose image starts with `arangodb` (e.g. `arangodb`, `arango`,
`villagerbot_db_1`). Use it as `CONTAINER` below. If `docker ps` shows no Arango container, check stopped ones with
`sudo docker ps -a`. The bot's `config.json` `hostName` may also point to another machine.

**2. Check the tools and version:**
```bash
sudo docker exec CONTAINER arangoexport --version
```

**3. Get the root password,** if you don't know it. It's either in the bot's `config.json` (`arangoDetails.password`)
or in the container's environment:
```bash
sudo docker inspect CONTAINER --format '{{range .Config.Env}}{{println .}}{{end}}' | grep -i ARANGO
```
Look for `ARANGO_ROOT_PASSWORD=...`. If you see `ARANGO_NO_AUTH=1`, authentication is off: use
`--server.authentication false` below and there's no password prompt. Keep the password to yourself.

**4. Optional sanity check:** list the databases and count the documents. `-it` lets it prompt for the password.
```bash
sudo docker exec -it CONTAINER arangosh \
  --server.endpoint tcp://127.0.0.1:8529 --server.authentication true --server.username root \
  --server.database villagerhaven \
  --javascript.execute-string 'db._collections().filter(c => !c.name().startsWith("_")).forEach(c => print(c.name(), c.count()))'
```

**5. Export inside the container:**
```bash
sudo docker exec -it CONTAINER arangoexport \
  --server.endpoint tcp://127.0.0.1:8529 \
  --server.authentication true \
  --server.username root \
  --server.database villagerhaven \
  --collection requests --collection archivedrequests \
  --collection blocks --collection reservations \
  --type jsonl --output-directory /tmp/vh-export --overwrite true
```
`127.0.0.1:8529` is correct here even if the port is mapped differently on the host, because this runs inside the container.

**6. Copy out and take ownership:**
```bash
sudo docker cp CONTAINER:/tmp/vh-export ~/vh-export
sudo chown -R "$USER" ~/vh-export
sudo docker exec CONTAINER rm -rf /tmp/vh-export
```

**If the bot itself also runs in Docker,** `lastpos.json` is inside the bot's container:
```bash
sudo docker exec BOT_CONTAINER sh -c 'find / -name lastpos.json 2>/dev/null'
sudo docker cp BOT_CONTAINER:/path/found/lastpos.json ~/vh-export/
```

**Common errors:**

| Message | Fix |
|---|---|
| `Could not connect to endpoint` | The server isn't listening on 8529 inside the container. Check the container's command or config (`sudo docker inspect CONTAINER`) for another port. |
| `401` / `not authorized` | Wrong password, or `--server.authentication true` is missing. |
| `database not found` | Run step 4 without `--server.database` and with `print(db._databases())` to list the names. |
| `unknown option` | An old client version. Paste the exact error (with the password removed) and I'll adapt the command. |

If `arangoexport` isn't on the VM at all but ArangoDB is remote, install the ArangoDB **client tools** for the matching
version, or run the export from any machine that can reach the database.

### 3.4 Record counts (for validating the import)

```bash
wc -l ~/vh-export/*.jsonl
```

### 3.5 Bundle it with `lastpos.json` and download

```bash
cp /path/to/bot/lastpos.json ~/vh-export/
tar czf ~/vh-export.tgz -C ~ vh-export
```

Then, from your PC:

```bash
gcloud compute scp INSTANCE_NAME:~/vh-export.tgz . --zone ZONE
```

Or in the Console SSH window: gear icon → **Download file** → `/home/YOUR_USER/vh-export.tgz`.

Extract it to **`d:\git\vh-bots\villager-bot\data-export\`**. That folder is git-ignored, because it contains member IDs.
Afterwards, delete the copies on the VM: `rm -r ~/vh-export ~/vh-export.tgz`.

## 4. Field mapping

### `requests` → `ActiveRequests`

| Legacy | New | Handling |
|---|---|---|
| `_key` | `UserId` | parse to ulong |
| `villager` | `VillagerKey` | must exist in the catalogue; otherwise reported |
| `timeStamp` | `SubmittedAt` | ISO string or epoch (the importer accepts both); missing → reported |
| `status` | `PulledAt` null/not-null | `ACCEPTED` → **archived as `Expired`** instead (stale pulls; decided). `REMOVED` → reported (not expected) |
| `acceptedTimeStamp` | `PulledAt` | |
| `helperUserId` | `HunterId` | |
| `channelId` | `ChannelId` | |
| `available` | `IsAvailable` | null → false |
| `pos` | — | **Ignored.** Kept requests are renumbered 1…N ordered by `timeStamp` (ties broken by legacy `pos`) (decided) |
| `_rev` | (staleness filter only) | Decoded to a last-write time (§4.1). Not stored. |
| — | `queue_position` sequence | `setval(N)`, so the next ticket is N + 1 |

### `archivedrequests` → `ArchivedRequests`

| Legacy | New |
|---|---|
| `userId` | `UserId` |
| `villager` | `VillagerKey` |
| `submissionTimeStamp` | `SubmittedAt` |
| `acceptedTimeStamp` | `PulledAt` |
| `archiveTimeStamp` | `ClosedAt` (missing → reported) |
| `helperUserId` | `HunterId` |
| `status` `COMPLETED` / `TIMEOUT` / `REMOVED` | `Outcome` (missing or unknown → reported) |

`blocks` and `reservations` are not imported.

### 4.1 Staleness wipe (last-write time from `_rev`)

The legacy data has no "last updated" field, but ArangoDB `_rev` values are hybrid-logical-clock stamps. Decoding the
custom base64 (alphabet `-_A-Za-z0-9`) gives a 64-bit value whose upper bits (`value >> 20`) are **Unix milliseconds of
the document's last write**. This was validated on the 2026-09-28 export: no decoded time precedes the request's
`timeStamp`, and about a quarter match submission within seconds (never-edited requests).

Caveats:
- Bulk writes also bump `_rev`. Spikes on 2021-02-02 (474 rows) and 2024-02-15 (97 rows) look like `!statuswipe`
  runs. These only make a request look *newer*, so a cutoff never wipes something a member actually touched recently.
- A "touch" is any write: submit, `!change`, `!status`, or an admin bulk update.

Requests whose last write is older than the cutoff are archived as **`Expired`** (with `ClosedAt` = import time) rather than
kept active. Their members can simply request again. The cutoff is the tool option `--stale-days`, **365 by default (decided 2026-09-28)**.

Counts from the 2026-09-28 export (excluding the 3 stale pulls):

| Keep requests touched within | Kept | Of which available | Archived |
|---|---|---|---|
| 30 days | 22 | 12 | 5,092 |
| 90 days | 70 | 36 | 5,044 |
| 6 months | 202 | 102 | 4,912 |
| 1 year | 502 | 117 | 4,612 |
| 2 years | 790 | 117 | 4,324 |

### 4.2 First export findings (2026-09-28, test data only; final export at cutover)

- `requests` 5,117 (117 available, 3 `ACCEPTED` from 2021 whose channels are long gone); `archivedrequests` 18,995
  (12,763 COMPLETED / 5,952 TIMEOUT / 280 REMOVED; 280 rows lack `acceptedTimeStamp` and `helperUserId`, all
  REMOVED-type); `blocks` 92; `reservations` 13.
- All villager keys match the legacy enum. One key contains a non-ASCII letter (`RENÉE`); the catalogue stores the
  legacy key verbatim as an alias so it maps cleanly.
- Timestamps are ISO-8601 strings with either millisecond or microsecond precision.
- No duplicate user IDs or positions.
- The newest archive entry is from 2026-03-17 (staff haven't been pulling recently); the newest request is from the
  day of the export. The data is live, so the import is re-run on a final export at cutover.

## 5. Import tool

`src/VillagerBot.Migration`, a console app using the bot's own EF Core `DbContext`. The rules live in `ImportPlanner`
and are covered by tests.

| Option | Default | Meaning |
|---|---|---|
| `--export <dir>` | `data-export` | Folder holding `requests.jsonl` and `archivedrequests.jsonl` |
| `--stale-days <n>` | `365` | Archive requests not written to in the last *n* days |
| `--as-of <iso>` | now | Treat this instant as "now" (reproducible dry runs) |
| `--apply` | off | Write to the database in `ConnectionStrings__VillagerBot`. Applies migrations, refuses a database that already has requests, and imports in one transaction. |
| `--force` | off | Apply even if the report lists problem rows (they're skipped) |

Without `--apply` it's a dry run that prints the report and writes nothing. Ways to run it:

```bash
dotnet run --project src/VillagerBot.Migration -- --export data-export             # local dry run
docker compose -f deploy/compose.yaml run --rm villager-bot-migration              # dry run in Docker
docker compose -f deploy/compose.yaml run --rm villager-bot-migration --apply      # import into the suite's Postgres
```

Dry run on the 2026-09-28 export (`--as-of 2026-09-29`): 502 requests kept (117 available), 4,612 archived as stale,
3 stale pulls archived, and an archive of 12,763 Completed / 5,952 Timeout / 4,895 Removed. No problems.
With the `Expired` outcome (2026-10-02), the same run gives 12,763 Completed / 5,952 Timeout / **280 Removed** (the
legacy "member left" archives) / **4,615 Expired** (4,612 stale requests plus the 3 stuck pulls).

## 6. Cutover plan (outline)

1. **Test first:** a separate test Discord server plus a test bot application, running on a copy of the migrated data.
2. **No open request channels at switchover** (confirmed by the maintainer). Any request still marked pulled is
   archived as `Expired` by the import.
3. Stop the old bot, take the final export (same procedure as §3; no `lastpos.json` needed), and run the import with
   `--apply` (default 365-day staleness cutoff).
4. Start the new bot, which registers its commands. Complete the setup checklist (command-design §6.3), run
   `/admin panel` in `#villager-request`, and announce the change.
5. Keep the old VM and export for a week or two as a rollback, then shut down the GCP VM.
