# Hosting Options for the Villager Bot Rewrite

Researched 2026-09-28; decision revised 2026-09-29. Prices and free tiers change often (several changed this year), so
re-check pricing pages before committing.

## Decision (2026-09-29): OVHcloud VPS-1

**Chosen: OVHcloud VPS-1** in a US datacenter (Vint Hill VA or Hillsboro OR): 2 vCPU, **4 GB RAM**, 40 GB NVMe, public
IPv4 and daily automated backup included, about **$4.54/month** on a 12-month commitment or about **$6.46** month to
month. Everything runs as in the rest of this doc's architecture A: a gateway bot in Docker Compose with Postgres on
the same box. Setup: [deployment.md](deployment.md).

**Why not Oracle Always Free** (the original first choice, below): the new account got stuck in "provisioning" (a
widely reported issue that can take weeks to clear), which blocks upgrading to Pay As You Go, and every attempt to
create the Ampere instance failed with "Out of host capacity".

**Budget and sizing:** up to $10/month. Postgres uses about 100–150 MB and each .NET bot about 100–150 MB, so the
planned suite of 4–5 bots needs about 1 GB. 1 GB plans would be tight; 2 GB or more is comfortable. The Docker build
works on ARM and x86 alike.

| Option (checked 2026-09-29) | Specs | Price/month | Verdict |
|---|---|---|---|
| **OVHcloud VPS-1** | 2 vCPU, 4 GB, 40 GB, IPv4 + daily backup included | ~$4.54 (12-mo) / ~$6.46 (monthly) | ✅ Chosen: most headroom by far within budget |
| Linode (Akamai) Nanode | 1 vCPU, 1 GB, 25 GB | $5 (2 GB: $12; 4 GB: $24) | Viable for this bot alone; tight for the suite. Nicest UX and a $100 trial credit. |
| GCP e2-micro (free tier) | 2 shared vCPU, 1 GB | ~$3.65 (IPv4 only) | Least migration effort; too small for the suite |
| AWS Lightsail | 2 vCPU, 1 GB / 2 GB | $7 / $12 | 1 GB tight; 2 GB over budget |
| DigitalOcean / Vultr | 1 GB | $5–6 (2 GB: $12) | Same RAM problem |
| Hetzner CX23 / CAX11 | 2 vCPU, 4 GB | ~€6 incl. IPv4 | Would suit, but every cost-optimized plan was sold out, after two price rises in 2026 |
| Railway (managed) | usage-based | ~$5–10 | No Docker Compose; cost grows with each bot |

The rest of this document is the original free-tier analysis (2026-09-28). It's kept for its reasoning about
architectures, serverless cold starts and databases, but its Oracle recommendation is **superseded**.

## Requirements that drive the choice

| Requirement | Implication |
|---|---|
| C# / .NET 10 (NetCord only targets `net10.0`) | Rules out JS-only platforms (Vercel functions and Cloudflare Workers have no .NET runtime) |
| Slash commands only; no message-content events needed | The bot **can** run either as an always-on **gateway** process or as a **stateless HTTP interactions endpoint** (serverless) |
| Discord requires an interaction response within **3 seconds** | Serverless cold starts are the main risk for HTTP mode |
| Existing ArangoDB data must be migrated | Needs a database the bot can reach. Moving to Postgres or SQLite is reasonable (see §4). |
| One guild, fewer than about 1k users/month, bursty low traffic | Tiny compute needs. Any free tier's quotas are more than enough; always-on and cold-start behavior matter more. |
| Credit card OK; small Linux VM OK; not self-hosted at home | VM-style free tiers are on the table |
| Current cost: GCP VM at about $20/month | Baseline to beat |

## Two architectures

**A. Gateway bot on a VM.** An always-on `GatewayClient` process (NetCord Generic Host + `AddDiscordGateway`).
This is the most direct path, the model most NetCord guides use, and gives instant responses with no cold starts. It can
use gateway events or cache later if needed, and can run SQLite or Postgres on the same box. You manage the OS
(updates, a systemd service, backups).

**B. HTTP interactions (serverless).** An ASP.NET Core app with `NetCord.Hosting.AspNetCore` and `UseHttpInteractions`.
Discord POSTs each interaction to your URL. There's nothing to patch and it scales to zero, but:
- cold starts compete with the 3-second deadline;
- it needs native **libsodium**;
- there is no gateway (no events, cache, or presence);
- everything goes through REST;
- the database must be external and reachable from the function.

This bot's features (channel creation, DMs, member lookups) all work over REST, so B is technically feasible.

## Options compared

| Option | Arch | Cost | Always-on | Pros | Cons / risks |
|---|---|---|---|---|---|
| **Oracle Cloud Always Free**: Ampere A1 (up to 2 OCPU / 12 GB total) or AMD micro (2× 1/8 OCPU, 1 GB) | A | **$0** | Yes | Most generous free VM by far. 200 GB block storage, 10 TB egress. A1 can comfortably run .NET + Postgres. No time limit. | A1 capacity is sometimes unavailable in popular home regions. **Idle reclamation:** Always Free instances with 95th-percentile CPU, network, *and* (A1) memory all under 20% for 7 days may be reclaimed, and a quiet bot fits that profile. Upgrading the account to Pay-As-You-Go (card on file, still $0 for Always Free usage) is the commonly used way to avoid reclamation, but the docs don't state that exemption explicitly, so treat it as unconfirmed. Oracle **halved** the A1 allowance in June 2026 without much notice, so terms can change. More setup friction than GCP. |
| **GCP e2-micro** (your current provider, downsized) | A | **≈ $3.65/mo** | Yes | Least migration effort: same console, billing, and SSH tooling you use today. The e2-micro VM is free in `us-west1`, `us-central1`, and `us-east1` with 30 GB **standard** persistent disk and 1 GB egress/month. 1 GB RAM is fine for a .NET bot + SQLite. | **Not quite free:** Google's current network pricing makes in-use external IPv4 addresses free for only **1 hour/month**, then $0.005/h. Several third-party guides still say the IP is free, but the official page says otherwise. Must pick *standard* disk (the default *balanced* disk is billed). 1 GB RAM rules out running ArangoDB or Postgres comfortably alongside. |
| **Google Cloud Run** (+ external DB) | B | **$0** at this volume | Scale-to-zero | Free tier: 2M requests, 180k vCPU-s, 360k GiB-s/month, far beyond need. Container-based and fully managed. Same GCP account. | **Cold starts vs the 3 s deadline:** a .NET container waking from zero can take 1–3+ s before your code even runs. Mitigations: Native AOT, startup CPU boost, a small image. `min-instances=1` fixes it but is no longer free. The DB must be external (Firestore free tier, Supabase, or Neon). Requires libsodium in the image. |
| **Azure Container Apps** (+ external DB) | B | **$0** at this volume | Scale-to-zero | Monthly free grant (180k vCPU-s, 360k GiB-s, 2M requests). First-class .NET. | Same cold-start risk as Cloud Run. New provider and account. |
| **Azure VM (B1s)** | A | $0 for **12 months only** | Yes | Simple VM with the free-account credit. | Becomes paid after year one. |
| **Azure Functions (Consumption)** | B | ~$0 | Scale-to-zero | .NET-native. | Cold starts are reported at up to 30 s, which is too slow for a 3 s deadline. **Not recommended.** |
| **AWS** | A/B | Credits only | — | — | Since July 2025, new accounts get up to $200 of credits that expire after 6 months. The 12-month free EC2 tier is gone for new accounts. **Not free long-term.** |
| **Fly.io** | A | ~$2–5/mo | Yes | Nice deploy UX. | No free tier for new accounts (trial only). |
| **Railway** | A | $1/mo credit on the free plan | — | Easy deploys. | $1 of credit won't cover an always-on process; Hobby is $5/mo. |
| **Render** | A/B | Free web service only | No | — | The free tier has no background workers (a gateway bot needs a worker, $7/mo). Free web services sleep after 15 min and take about **1 minute** to wake, which breaks the 3 s deadline. |
| **Koyeb** | B | Free instance | No | 512 MB free web service. | Scales to zero after 1 h idle (can't be disabled), so wake-ups hit the 3 s deadline. Can't run worker services. |
| **Vercel / Cloudflare Workers** | B | Free | — | You know Vercel. | No .NET runtime. Would require rewriting in JS/TS or Python, which defeats the NetCord goal. **Not viable.** |

## Oracle idle reclamation: what "idle" means

Oracle measures **resource utilization, not activity**. Commands received don't count directly. Per the
Always Free docs, an instance is considered idle if, over a 7-day window, **all** of the following are true:

- 95th-percentile **CPU** utilization < 20%
- **Network** utilization < 20%
- **Memory** utilization < 20% (checked on A1 shapes only)

A Discord bot idles at around 1% CPU, and handling a few commands a week (or even a few hundred) barely moves a 95th
percentile, so **traffic alone won't keep it safe**. Because *all three* conditions must hold, keeping any **one**
above 20% makes the instance not idle. Ways to do that:

1. **Upgrade the account to Pay-As-You-Go.** Always Free resources stay free. This is widely reported to exempt
   the tenancy from idle reclamation, but Oracle's docs present the policy only for Always Free resources and don't
   state the exemption explicitly. It's the usual first step, and a card is on file anyway.
2. **Keep memory above 20% on an A1 instance.** Right-size the VM so the normal workload crosses the line. For example,
   a 1 OCPU / 6 GB A1 needs about 1.2 GB in use; .NET + Postgres (or a modest Postgres `shared_buffers`) can reach that
   naturally. This is the most controllable lever. Check the instance's memory metric in the OCI console after
   deploying to confirm it reads above 20%.
3. Artificial CPU load ("keep-alive" scripts) also works but wastes resources. Treat it as a last resort.

The docs don't say what reclamation does to the instance's disk, so keep off-box DB backups either way, so you
could rebuild anywhere.

## Recommendation

1. **First choice: Oracle Cloud Always Free, gateway bot (architecture A).**
   - Use an A1 instance with 1–2 OCPU and 6–12 GB.
   - Run the bot as a systemd service or Docker container, with Postgres or SQLite on the same VM.
   - Upgrade the account to Pay-As-You-Go immediately. That's the commonly recommended guard against idle reclamation, and
     it also tends to make A1 capacity easier to get. Set a **budget alert at $1** so any accidental paid usage is noticed.
   - Keep automated DB backups off-box (e.g. OCI Object Storage, which has a free allowance), since free-tier terms can change.

2. **Fallback / lowest effort: stay on GCP and move to an e2-micro (about $3.65/month).**
   This keeps your existing account and knowledge and cuts cost by about 80%, with no reclamation risk. Worth doing
   *immediately* even before the rewrite ships, if the current VM can be downsized. That depends on whether ArangoDB
   runs on it: 1 GB RAM is too little for ArangoDB, which is likely why the current VM is larger.

3. **If you'd rather have zero servers: Cloud Run + Supabase or Neon (architecture B).**
   Only choose this after a quick cold-start test of a NetCord HTTP container, ideally Native AOT. If the p99 wake time
   isn't comfortably under about 2 s, the occasional "The application did not respond" error will annoy users.

Design the code so both architectures stay possible. Keep business logic in services that don't depend on
`GatewayClient`, and use NetCord's `RestClient` for all Discord actions. Then switching between A and B is mostly
the `Program.cs` host setup and the context types (`ApplicationCommandContext` vs `HttpApplicationCommandContext`).

## Database options (for the Arango migration)

> **Decided 2026-09-28: PostgreSQL**, one server shared by the whole bot suite (3–4 more bots are planned), one
> database per bot, running on the same VPS as the bots (now an OVHcloud VPS-1 with 4 GB; see "Decision" above). A 1 GB
> machine can't comfortably host Postgres plus several .NET bots. See data-migration.md §1.


| Option | Cost | Fit |
|---|---|---|
| **SQLite on the VM** (EF Core) | $0 | Simplest for architecture A. The data volume is tiny. Back up the file off-box. |
| **Postgres on the VM** | $0 | Good on an Oracle A1. Heavier than needed on an e2-micro. |
| **Supabase free Postgres** | $0 | 500 MB DB; the project **pauses after 7 days of low activity**. A bot with daily traffic should stay awake, but a quiet week could pause it. Works for either architecture. |
| **Neon free Postgres** | $0 | Serverless Postgres with autosuspend. The first query after idle adds latency, which matters in architecture B. |
| **Firestore (GCP)** | $0 (1 GiB, 50k reads/day) | Document model close to Arango's. Pairs naturally with Cloud Run. Aggregate stats (`!helpers`, `!toprequesters`) are less convenient. |
| Keep ArangoDB | $0 self-hosted | Needs ≥ 1–2 GB RAM (fine on Oracle A1, not on an e2-micro). The .NET driver ecosystem is thin. Not recommended. |

Migration path in all cases: `arangoexport` each collection (`requests`, `archivedrequests`, `reservations`,
`blocks`) to JSON, then run a one-off C# import tool that maps enum keys and timestamps into the new schema.
Also carry over the `lastpos.json` value.

## Other Discord-side notes

- In HTTP mode the bot shows as offline (no presence). That's cosmetic, but staff may notice.
- Whichever architecture you choose, keep the bot token and DB credentials in environment variables or a secrets store, not in
  `appsettings.json` committed to git.

## Sources

2026-09-29 decision:
- OVHcloud US VPS range: <https://us.ovhcloud.com/vps/>
- OVH VPS pricing review (monthly prices, 2026 increase): <https://learnwithhasan.com/vps-providers/ovh-vps/>
- Akamai (Linode) pricing, North America: <https://www.akamai.com/cloud/pricing/north-america>
- Hetzner cost-optimized plans (sold out): <https://www.hetzner.com/cloud/cost-optimized/>
- Hetzner price adjustment, June 2026: <https://docs.hetzner.com/general/infrastructure-and-availability/price-adjustment/>
- AWS Lightsail pricing: <https://aws.amazon.com/lightsail/pricing/>

Original analysis (2026-09-28):

- Google Cloud free tier features: <https://docs.cloud.google.com/free/docs/free-cloud-features>
- Google Cloud network pricing (external IP free tier = 1 h/month): <https://cloud.google.com/vpc/network-pricing>
- Oracle Always Free resources & idle reclamation: <https://docs.oracle.com/en-us/iaas/Content/FreeTier/freetier_topic-Always_Free_Resources.htm>
- Oracle A1 allowance cut (InfoQ, Jul 2026): <https://www.infoq.com/news/2026/07/oracle-cloud-free-tier-limits/>
- AWS free tier change (Jul 2025): <https://infratally.com/articles/aws-free-tier-2026/>
- Azure free services / Container Apps grant: <https://azure.microsoft.com/en-us/pricing/free-services>, <https://learn.microsoft.com/en-us/azure/container-apps/billing>
- Fly.io free tier status: <https://www.saaspricepulse.com/blog/flyio-free-tier-2026>
- Railway plans: <https://docs.railway.com/pricing/plans>
- Render free tier: <https://render.com/docs/free>
- Koyeb free instance: <https://www.koyeb.com/docs/faqs/pricing>
- Supabase project pausing: <https://supabase.com/docs/guides/platform/free-project-pausing>
- Discord 3-second response & serverless cold starts: <https://github.com/pmgledhill102/gcp-discord-bot-go>
