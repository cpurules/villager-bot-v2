# Deployment & Cutover Runbook

How to stand up the bot suite on an Oracle Cloud Always Free VM and switch Villager Haven from the old Java bot to the
new one. Background and the hosting decision: [hosting-options.md](hosting-options.md). Discord-side setup:
[command-design.md §6.3](command-design.md#63-bot-setup-brand-new-application).

Each phase ends with a **✅ Check** so you know it worked before moving on. Phases 1–5 can be done any time before cutover.
Phase 7 is the switchover itself (about 30–45 minutes).

---

## Phase 1: Oracle Cloud account

1. Sign up at <https://signup.cloud.oracle.com>. Pick a **home region** close to you (US: Ashburn, Phoenix, San Jose,
   Chicago). The home region can't be changed later, and Always Free resources only live there.
2. **Upgrade to Pay As You Go** (Billing → Upgrade and Manage Payment). Always Free resources stay free. This is the
   commonly recommended guard against idle reclamation, and it tends to make Ampere capacity easier to get
   (hosting-options.md, "Oracle idle reclamation").
3. **Set a budget alert** so any accidental paid usage is noticed: Billing → Budgets → Create Budget, amount **$1**,
   alert at 100% of actual spend, sent to your email.

✅ **Check:** the Billing page shows Pay As You Go, and the budget exists.

## Phase 2: Create the VM

Compute → Instances → **Create instance**:

| Setting | Value |
|---|---|
| Name | `vh-bots` |
| Image | **Canonical Ubuntu 24.04** (the aarch64 build is picked automatically for Ampere) |
| Shape | Change shape → Ampere → **VM.Standard.A1.Flex**, **2 OCPU, 12 GB** (the whole Always Free allowance; 1 OCPU / 6 GB also works) |
| Networking | Create a new VCN with a public subnet; **assign a public IPv4 address** (needed for SSH) |
| SSH keys | Upload your public key, or let Oracle generate a pair and **download the private key now** (it isn't shown again) |
| Boot volume | Default (~47 GB) is plenty. Always Free covers 200 GB in total. |

- **"Out of host capacity"** is common for Ampere in busy regions. Retry later, try another availability domain in the
  same region, or reduce to 1 OCPU / 6 GB. Pay As You Go accounts hit this less often.
- **No inbound ports are needed.** The bots only make outgoing connections to Discord, and Postgres isn't reachable
  from outside Docker. Leave the default security list (SSH, port 22, only). Don't open anything else.

✅ **Check:** from your PC, `ssh ubuntu@<public-ip>` (add `-i path\to\key` if Oracle generated it) gets you a prompt.

## Phase 3: Prepare the VM

On the VM:

```bash
sudo apt update && sudo apt full-upgrade -y
sudo apt install -y git unattended-upgrades rclone
sudo dpkg-reconfigure -plow unattended-upgrades        # answer Yes: automatic security updates

# Docker Engine + Compose plugin (official script; supports Ubuntu on arm64)
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker ubuntu
sudo timedatectl set-timezone America/Chicago           # optional: log timestamps in your time zone
exit                                                    # log out and back in so the docker group applies
```

✅ **Check:** after logging back in, `docker run --rm hello-world` prints "Hello from Docker!" and
`docker compose version` prints a version.

## Phase 4: Get the code onto the VM

**If the repo has a remote** (GitHub/GitLab), clone it. For a private repo, add a read-only **deploy key**: run
`ssh-keygen -t ed25519 -f ~/.ssh/deploy_key -N ""` on the VM, add `~/.ssh/deploy_key.pub` to the repo's deploy keys,
then clone with that key:

```bash
mkdir -p ~/vh-bots && cd ~/vh-bots
GIT_SSH_COMMAND="ssh -i ~/.ssh/deploy_key" git clone git@<host>:<you>/villager-bot.git
cd villager-bot && git config core.sshCommand "ssh -i ~/.ssh/deploy_key"
```

**No remote yet?** Copy it from your PC instead (PowerShell, in `d:\git\vh-bots`). This excludes build output and data:

```powershell
tar --exclude=bin --exclude=obj --exclude=data-export --exclude=.vs --exclude=deploy/.env -czf villager-bot.tgz villager-bot
scp villager-bot.tgz ubuntu@<public-ip>:~/
# then on the VM: mkdir -p ~/vh-bots && tar xzf ~/villager-bot.tgz -C ~/vh-bots
```

Then create the secrets file. **Use the new live bot's token**, not the test bot's, and fresh passwords (letters and
digits only):

```bash
cd ~/vh-bots/villager-bot/deploy
cp .env.example .env && chmod 600 .env
nano .env        # POSTGRES_PASSWORD, VILLAGER_BOT_DB_PASSWORD, VILLAGER_BOT_DISCORD_TOKEN, VILLAGER_BOT_ENVIRONMENT=Production
```

Generate a password on the VM with `openssl rand -hex 24`.

✅ **Check:** `ls -l .env` shows `-rw-------`, and `grep -c '=$' .env` prints `0` (nothing left blank).

## Phase 5: Start Postgres and build the images

```bash
cd ~/vh-bots/villager-bot/deploy
docker compose up -d --wait postgres       # first start creates the villager_bot database and login
docker compose build                       # builds the bot for ARM on the VM (a few minutes the first time)
```

**Don't start the bot yet.** Its database is empty until the cutover import (Phase 7).

✅ **Check:** `docker compose ps` shows `postgres` as **healthy**, and
`docker compose exec postgres psql -U postgres -c '\l'` lists `villager_bot`.

## Phase 6: Nightly backups

`deploy/backup.sh` dumps every bot database (compressed, restorable with `pg_restore`) into `deploy/backups/`, and keeps
14 days.

**Off-box copies (recommended):** Always Free includes 20 GB of Object Storage.

1. Console → Storage → Buckets → Create bucket `vh-bots-backups` (Standard tier, private).
2. Configure rclone on the VM with `rclone config`: new remote named `oci`, type **Oracle Object Storage**, provider
   **instance_principal_auth** if you've set up a dynamic group and policy for the VM, otherwise **user_principal_auth**
   with an API key (Profile → API keys → Add API key; rclone reads `~/.oci/config`).
3. Test it with `rclone lsd oci:`, which should list the bucket.

Schedule it with `crontab -e`:

```cron
30 3 * * * RCLONE_REMOTE=oci:vh-bots-backups /home/ubuntu/vh-bots/villager-bot/deploy/backup.sh >> /home/ubuntu/backup.log 2>&1
```

(Leave out `RCLONE_REMOTE=…` to keep local copies only.)

**Restore** into an empty database. Stop the bot first:

```bash
docker compose stop villager-bot
docker compose exec -T postgres pg_restore -U postgres --clean --if-exists -d villager_bot < backups/villager_bot-<stamp>.dump
docker compose start villager-bot
```

✅ **Check:** run `./backup.sh` once by hand. A `.dump` file appears in `deploy/backups/` (and in the bucket, if configured).

---

## Phase 7: Cutover

Pick a quiet time and tell staff first (there's a draft announcement at the end).

### 7.1 Discord setup for the live bot (can be done ahead of time)

Follow [command-design.md §6.3](command-design.md#63-bot-setup-brand-new-application). In short:

1. Developer Portal: the new **live** application, with **Public Bot off** and **all privileged intents off**.
2. Invite it to Villager Haven with the `bot` and `applications.commands` scopes.
3. Bot role, server-wide: View Channels, Send Messages, Embed Links, Read Message History, **Manage Channels**,
   **Manage Roles**, **Manage Messages**, Use External Emojis.
4. On the main request category (`720676376335745034`): allow the bot's role View Channel, Send Messages, Read Message
   History, Manage Channels, Manage Permissions.
5. In `#villager-request` and the requests, mod-mail, starter-kit, staff-log and timeout-log channels: allow the bot's
   role View Channel, Send Messages, Embed Links (wherever @everyone can't see them).

### 7.2 Freeze and export the old data

1. Make sure **no request channels are open** (decided; any stragglers are archived as Removed by the import).
2. **Stop the old bot** on the GCP VM, so the data can't change during the export.
3. Export exactly as before ([data-migration.md §3](data-migration.md#3-legacy-data-export-run-on-the-current-gcp-vm)).
   `lastpos.json` isn't needed.
4. Copy the export to the new VM. From your PC:
   `scp -r <export folder>\* ubuntu@<public-ip>:~/vh-bots/villager-bot/data-export/`
   (create the folder first with `mkdir -p ~/vh-bots/villager-bot/data-export` on the VM).

### 7.3 Import

```bash
cd ~/vh-bots/villager-bot/deploy
docker compose run --rm villager-bot-migration              # dry run: read the report
docker compose run --rm villager-bot-migration --apply      # import (refuses a non-empty database)
```

✅ **Check:** the report shows **Problems: none**, and the kept/archived counts look like the test run (roughly 500
kept, about 23,600 archived, higher by whatever was added since). If there are problems, stop and look before using
`--force`.

### 7.4 Start the bot

```bash
docker compose up -d villager-bot
docker compose logs -f villager-bot        # Ctrl+C to stop watching
```

✅ **Check** in the logs:
- `Startup check: all configured channels and categories are visible to the bot.` If you see `can't see …` lines instead,
  fix those channel permissions (7.1 step 5) and restart with `docker compose restart villager-bot`.
- No errors mentioning the token or the database.

### 7.5 Discord finishing steps

1. **Server Settings → Integrations → the new bot**: grant the hidden staff commands.

   | Command | Grant to |
   |---|---|
   | `/queue`, `/lookup`, `/stats`, **Request Info**, **Request History** | Haven Hunter, Moderator, Hunter Hiatus, Haven |
   | `/pull`, `/close` | Haven Hunter, Moderator, Haven |
   | `/admin` | Haven (admins see it by default) |

   The bot enforces these tiers itself too (`/stats hunters` stays Haven-only), so this setting only controls who
   *sees* the commands.
2. Run **`/admin panel`** in any channel. The panel appears in `#villager-request`.
3. Remove or update any pinned instructions that mention `!villager`, `!status` and so on.
4. Smoke test with a staff account: `/queue list` in the requests channel, a `/lookup` on yourself, `/stats helped`.

### 7.6 Afterwards

- **Keep the old GCP VM stopped but not deleted** for 1–2 weeks as a rollback. To roll back: stop the new bot
  (`docker compose stop villager-bot`), then start the old one. Requests made on the new bot in between wouldn't carry over.
- Once you're confident, **delete the GCP VM and its disk** so billing stops. Also check for any leftover static IP
  and delete it too.
- Kick the old bot from the server once the new one is settled.

---

## Day-to-day operations

| Task | Command (in `~/vh-bots/villager-bot/deploy`) |
|---|---|
| Watch logs | `docker compose logs -f villager-bot` |
| Restart | `docker compose restart villager-bot` |
| Deploy an update | `git pull && docker compose up -d --build villager-bot` (database migrations apply on startup) |
| Status | `docker compose ps` |
| SQL console | `docker compose exec postgres psql -U postgres -d villager_bot` |
| Manual backup | `./backup.sh` |
| Disk space | `df -h /` and `docker system df` (clean old images with `docker image prune`) |

Containers restart automatically after a crash or a VM reboot (`restart: unless-stopped`, and Docker starts on boot).

**Idle reclamation:** after the first week, open the instance's **Metrics** in the Oracle console and check the memory
utilization graph. It should sit above 20%. If it doesn't, and the account isn't Pay As You Go, see
hosting-options.md for options.

---

## Draft staff announcement

> 📣 **Villager Bot is moving to slash commands!** Discord is retiring the permission our old `!` commands relied on, so
> Villager Bot has been rebuilt.
>
> **Members:** head to #villager-request and press **Request a villager**, or use `/request` anywhere (including DMs
> with the bot). Use **My request** or `/request` to check your place in the queue, change your villager, or mark yourself
> available when you have an open plot. `/modmail` and `/starterkit` work as before. Everyone who was waiting in the queue
> keeps their place (in the same order), as long as their request was active in the last year.
>
> **Haven Hunters:** `/queue list` in #requests (there's a **Pull a request…** menu right on the list), `/pull user`,
> `/pull villager`, `/pull next`, and the ✅ Completed / ⏱️ Timed out buttons in each request channel. Look people up
> with `/lookup` or by right-clicking them → Apps. Reservations and blocks have been retired.
