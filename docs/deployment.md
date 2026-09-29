# Deployment & Cutover Runbook

How to stand up the bot suite on an OVHcloud VPS and switch Villager Haven from the old Java bot to the
new one. Background and the hosting decision: [hosting-options.md](hosting-options.md). Discord-side setup:
[command-design.md §6.3](command-design.md#63-bot-setup-brand-new-application).

Placeholders like `<vps-ipv4>` mean "replace this, **including the angle brackets**": `<vps-ipv4>` becomes
`40.160.36.231`, not `<40.160.36.231>`.

Each phase ends with a **✅ Check** so you know it worked before moving on. Phases 1–5 can be done any time before cutover.
Phase 7 is the switchover itself (about 30–45 minutes).

---

## Phase 1: Order the VPS

Decided 2026-09-29: **OVHcloud VPS-1** (2 vCPU, 4 GB RAM, 40 GB NVPSe, public IPv4, daily automated backup included;
about $4.54/month with a 12-month commitment or about $6.46 month to month). Why: hosting-options.md, "Decision".

1. Create an account at <https://us.ovhcloud.com> (the US site bills in USD). New accounts are sometimes held for a
   manual identity check. That usually takes under a day; answer any email from OVH promptly.
2. Order **VPS → VPS-1** and configure:

   | Setting | Value |
   |---|---|
   | Location | A **US** datacenter: **US-East (Vint Hill, VA)** or **US-West (Hillsboro, OR)**. The default may be in Europe, so check it. |
   | Image / OS | **Ubuntu 24.04** (plain OS, no preinstalled apps) |
   | SSH key | If the order form offers it, paste your public key (see below). If not, OVH emails login details instead; Phase 3 switches to the key. |
   | Commitment | 12 months is cheapest; month to month costs a bit more but can be cancelled any time |
   | Options | None needed. Automated backup is already included in this range. |

   Your public key, created 2026-09-29 for this server, is `C:\Users\<you>\.ssh\id_ed25519_vhbots.pub`.
   Print it with `type $env:USERPROFILE\.ssh\id_ed25519_vhbots.pub` in PowerShell.
3. When the VPS is delivered (minutes to hours), OVH emails its **IPv4 address** and the login user (normally
   `ubuntu`). Both are also shown in the OVH Control Panel under **Bare Metal Cloud → Virtual private servers**.

✅ **Check:** the Control Panel shows the VPS as **running**, with an IPv4 address.

## Phase 2: First login and SSH setup

Add a shortcut to `~/.ssh/config` on your PC (`C:\Users\<you>\.ssh\config`), so plain `ssh vh-bots` works:

```
Host vh-bots
    HostName <vps-ipv4>
    User ubuntu
    IdentityFile ~/.ssh/id_ed25519_vhbots
    IdentitiesOnly yes
```

- **If you added the key when ordering:** `ssh vh-bots` logs straight in.
- **If you got a password instead:** log in once with it (`ssh ubuntu@<vps-ipv4>`; set a new password if asked), then
  install your key from PowerShell on your PC:

  ```powershell
  type $env:USERPROFILE\.ssh\id_ed25519_vhbots.pub | ssh ubuntu@<vps-ipv4> "mkdir -p ~/.ssh && chmod 700 ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"
  ```

**Then turn off password logins.** Unlike a cloud VPS with a firewall in front, a VPS is reachable on every port, and
bots constantly try SSH passwords. On the VPS:

```bash
echo -e "PasswordAuthentication no\nKbdInteractiveAuthentication no\nPermitRootLogin no" | sudo tee /etc/ssh/sshd_config.d/00-hardening.conf
sudo sshd -t && sudo systemctl reload ssh
```

Keep this session open, and test `ssh vh-bots` from a **new** terminal before closing it, so a mistake can't lock you out.
(If you ever are locked out: Control Panel → your VPS → **KVPS** console.)

✅ **Check:** `ssh vh-bots` works with the key, and `ssh -o PubkeyAuthentication=no ubuntu@<vps-ipv4>` is refused
with "Permission denied (publickey)".

## Phase 3: Prepare the VPS

On the VPS:

```bash
sudo apt update && sudo apt full-upgrade -y
sudo apt install -y unattended-upgrades rclone
sudo dpkg-reconfigure -plow unattended-upgrades        # answer Yes: automatic security updates

# Firewall: only SSH in. The bots only make outgoing connections, and Postgres isn't published outside Docker.
sudo ufw allow OpenSSH
sudo ufw --force enable

# Docker Engine + Compose plugin (official script)
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker ubuntu
sudo timedatectl set-timezone America/Chicago           # optional: log timestamps in your time zone
exit                                                    # log out and back in so the docker group applies
```

Docker adds its own firewall rules for *published* ports, which bypass ufw. That's fine here, because the production
compose file publishes none. Never add a `ports:` entry for Postgres in `compose.yaml`; that's what `compose.dev.yaml`
is for, on your PC.

✅ **Check:** after logging back in, `docker run --rm hello-world` prints "Hello from Docker!", `docker compose version`
prints a version, and `sudo ufw status` shows only OpenSSH allowed.

## Phase 4: Get the code onto the VPS

**Recommended: GitHub Actions** (§4a below). The Deploy workflow copies the tested code to the VPS and builds it, so the
VPS needs no git access. Otherwise use one of the manual options in §4b. Either way, finish with the secrets file (§4c).

### 4a. GitHub Actions setup (one time)

Workflows: `.github/workflows/ci.yml` (build and test on every push and pull request) and `.github/workflows/deploy.yml`
(manual deploy). The deploy connects to the VPS over SSH with its own key, so create one just for it. In PowerShell:

```powershell
ssh-keygen -t ed25519 -f vh-deploy -C "github-actions-deploy"   # press Enter twice: no passphrase
type vh-deploy.pub | ssh ubuntu@<vps-ipv4> "cat >> ~/.ssh/authorized_keys"
ssh-keyscan -t ed25519 <vps-ipv4>        # copy the output line for VM_SSH_KNOWN_HOSTS
```

In the GitHub repo: **Settings → Environments → New environment** `production` (optionally add yourself as a required
reviewer, so every deploy needs a click to approve). Then, in that environment:

| Kind | Name | Value |
|---|---|---|
| Secret | `VM_SSH_KEY` | Contents of the private key file `vh-deploy` (the whole file, including the BEGIN/END lines) |
| Secret | `VM_SSH_KNOWN_HOSTS` | The `ssh-keyscan` output line. This pins the VPS's identity, so the workflow can't be tricked into deploying elsewhere. |
| Variable | `VM_HOST` | The VPS's public IP |
| Variable | `VM_USER` | `ubuntu` |

Then delete the local `vh-deploy` files (the key only needs to live in GitHub and in the VPS's `authorized_keys`).

**Deploying:** Actions → **Deploy** → Run workflow, and choose what happens to the bot after the build:

| `bot` input | Effect |
|---|---|
| `leave-as-is` (default) | Copies code, starts Postgres if needed, builds the images. **The bot isn't started, stopped or restarted.** If it's already running, it keeps running the previous build. |
| `start-or-restart` | Same, then starts the bot on the new build (or restarts it), checks it's still up 20 seconds later, and shows its startup log. Fails the run if the bot exited. |
| `stop` | Same, then stops the bot. |

> ⚠️ **Until cutover (Phase 7), only ever choose `leave-as-is`.** Before the import, the live database is empty. A
> started bot would register its commands in Villager Haven and accept requests into an empty queue, alongside the old bot.

The deploy job only runs after the CI tests pass. The same script can be run by hand on the VPS:
`deploy/remote-deploy.sh leave-as-is|start-or-restart|stop`.

### 4b. Manual alternatives

**If the repo has a remote** (GitHub/GitLab), clone it. For a private repo, add a read-only **deploy key**: run
`ssh-keygen -t ed25519 -f ~/.ssh/deploy_key -N ""` on the VPS, add `~/.ssh/deploy_key.pub` to the repo's deploy keys,
then clone with that key:

```bash
mkdir -p ~/vh-bots && cd ~/vh-bots
GIT_SSH_COMMAND="ssh -i ~/.ssh/deploy_key" git clone git@<host>:<you>/villager-bot.git
cd villager-bot && git config core.sshCommand "ssh -i ~/.ssh/deploy_key"
```

**No remote yet?** Copy it from your PC instead (PowerShell, in `d:\git\vh-bots`). This excludes build output and data:

```powershell
tar --exclude=bin --exclude=obj --exclude=data-export --exclude=.vs --exclude=deploy/.env -czf villager-bot.tgz villager-bot
scp villager-bot.tgz ubuntu@<vps-ipv4>:~/
# then on the VPS: mkdir -p ~/vh-bots && tar xzf ~/villager-bot.tgz -C ~/vh-bots
```

### 4c. Secrets file (on the VPS, either way)

Create the secrets file. **Use the new live bot's token**, not the test bot's, and fresh passwords (letters and
digits only):

```bash
cd ~/vh-bots/villager-bot/deploy
cp .env.example .env && chmod 600 .env
nano .env        # POSTGRES_PASSWORD, VILLAGER_BOT_DB_PASSWORD, VILLAGER_BOT_DISCORD_TOKEN, VILLAGER_BOT_ENVIRONMENT=Production
```

Generate a password on the VPS with `openssl rand -hex 24`.

✅ **Check:** `ls -l .env` shows `-rw-------`, and `grep -c '=$' .env` prints `0` (nothing left blank).

## Phase 5: Start Postgres and build the images

**With GitHub Actions:** run **Deploy** with `bot: leave-as-is`. That does all of this phase. Otherwise, by hand:

```bash
cd ~/vh-bots/villager-bot/deploy
docker compose up -d --wait postgres       # first start creates the villager_bot database and login
docker compose build                       # builds the bot for ARM on the VPS (a few minutes the first time)
```

**Don't start the bot yet.** Its database is empty until the cutover import (Phase 7).

✅ **Check:** `docker compose ps` shows `postgres` as **healthy**, and this lists a database named `villager_bot`
(owner `villager_bot`):

```bash
docker compose exec postgres psql -U postgres -c '\l'
```

## Phase 6: Nightly backups

There are two layers:

- **OVH automated backup** (included): a daily snapshot of the whole VPS, keeping the previous day. It's good for
  "the server died", but it's only one day deep and lives with OVH.
- **`deploy/backup.sh`** (set up here): dumps every bot database (compressed, restorable with `pg_restore`) into
  `deploy/backups/`, and keeps 14 days.

**Optional off-VPS copies:** Backblaze B2's free tier (10 GB) is plenty for these dumps.

1. Create a Backblaze account → B2 → **Create a bucket** `vh-bots-backups` (private) → **Application Keys → Add a
   New Application Key** limited to that bucket, with read and write access.
2. On the VPS: `rclone config` → new remote named `b2`, type **Backblaze B2**, and enter the key ID and application key.
3. Test it with `rclone lsd b2:`, which should list the bucket.

Schedule it with `crontab -e`:

```cron
30 3 * * * RCLONE_REMOTE=b2:vh-bots-backups /home/ubuntu/vh-bots/villager-bot/deploy/backup.sh >> /home/ubuntu/backup.log 2>&1
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
4. Copy the export to the new VPS. From your PC:
   `scp -r <export folder>\* ubuntu@<vps-ipv4>:~/vh-bots/villager-bot/data-export/`
   (create the folder first with `mkdir -p ~/vh-bots/villager-bot/data-export` on the VPS).

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

Run **Deploy** with `bot: start-or-restart` (or on the VPS: `docker compose up -d villager-bot`), then watch the logs:

```bash
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
| Deploy an update | GitHub → Actions → **Deploy** → `start-or-restart` (by hand: `git pull && ./remote-deploy.sh start-or-restart`). Database migrations apply on startup. |
| Status | `docker compose ps` |
| SQL console | `docker compose exec postgres psql -U postgres -d villager_bot` |
| Manual backup | `./backup.sh` |
| Disk space | `df -h /` and `docker system df` (clean old images with `docker image prune`) |

Containers restart automatically after a crash or a VPS reboot (`restart: unless-stopped`, and Docker starts on boot).

**Updates and reboots:** unattended-upgrades installs security fixes automatically. Some need a reboot
(`/var/run/reboot-required` exists); reboot when convenient with `sudo reboot`. Postgres and the bot come back on
their own.

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
