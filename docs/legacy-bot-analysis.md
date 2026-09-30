# Legacy Villager Bot: Command & Functionality Analysis

Source: `D:\git\_vh-bots_old\villager-bot\` (Java 8, JDA 4.1.1, JDA-Utilities 3.0.3, ArangoDB driver 6.7).
Everything below was derived from the code, not the command names. Almost all logic lives in
`listener/ChatListener.java`, with persistence in `data/DataStore.java`.

## 0. How the bot works today

- The bot has a single `onMessageReceived` listener that parses `!` prefixes with `startsWith`. **Every command
  depends on reading message content**, so every command breaks without the Message Content intent.
- Commands fall into two groups:
  - **DM commands** (sent to the bot in a DM) are the member-facing request flow.
  - **Server-channel commands** (sent in a guild text channel) are the staff tooling.

  The same word can mean different things in each place (`!request`).
- **Confirmations use reaction menus** (JDA-Utilities `ButtonMenu` + `EventWaiter`): the bot posts a message,
  adds ✅/❌ (or custom emoji) reactions, waits **30 s**, then deletes the menu.
- **Permission failures are silent.** In guild channels, an unauthorized or wrong-channel command just has
  the user's message deleted.
- Everything is hardcoded: guild, channel, category, role, and emoji IDs are all string literals in
  `ChatListener`.
- Gateway setup: `GUILD_MEMBERS` intent, full member chunking and caching, presence "Villager Pairing".
- Other state: the request **position counter** lives in a local file `lastpos.json`, and the **freeze** flag is
  in memory (lost on restart).
- Times are displayed in `America/Chicago`.

## 1. Roles & permission tiers

Role names confirmed by the maintainer (2026-09-28).

| Role ID | Role | Used in tier(s) |
|---|---|---|
| `719738444498337855` | **Villagers** | *Villager* |
| `720682820812538067` | **Haven Hunter** (the helpers who fulfil requests) | Helper+, HelperOrModTeam+ |
| `719737002878042134` | **Moderator** | Helper+, Moderator+, HelperOrModTeam+ |
| `761041280355532820` | **Haven** (server admin role) | Helper+, Moderator+, Manager+, HelperOrModTeam+ |
| `724720929728495617` | *No longer exists* (was a "mod team" role) | HelperOrModTeam+ only. **Drop it**: HelperOrModTeam+ now collapses to Helper+. |
| `765007555113648148` | **Hunter Hiatus** (inactive hunter) | Hiatus (informational commands only) |

"Manager+" in the code means the **Haven** admin role.

Tier definitions (a member qualifies if they have **any** listed role):

| Tier | Roles |
|---|---|
| **Villager** | Villager |
| **Helper+** | Helper, Moderator, Manager |
| **Moderator+** | Moderator, Manager. *Defined but never used.* |
| **Manager+** | Manager |
| **HelperOrModTeam+** | Helper, Mod team, Moderator, Manager |
| **Hiatus** | Hunter Hiatus |

There is also a hardcoded **user** ID `245619094240362496` that can bypass the freeze in DMs. It's presumably
the original developer.

## 2. Channels, categories, emoji & other IDs

| ID | Purpose (from code) |
|---|---|
| Guild `719579184023863378` | The Villager Haven server |
| `900848547845537833` | **Requests channel**: where `!requests`, `!select`, `!reserve`, `!unreserve` must be run |
| `812068854884335677` | **#haven** (staff): where `!block`/`!blocks` must be run; also receives auto-block notices |
| `765984666531659806` | **Mod mail** inbox channel |
| `795365351348764702` | **Starter kit** requests channel ("Events Team") |
| `719902403058466818` | **Staff alert/log** channel (DM failures, "user left" notices) |
| `725528965347016725` | **Timeout log** channel: receives the closed-channel transcript image |
| `739368144916840478` | Rules/info channel linked in the "you need the Villager role" message |
| `760542387923058698` | **Template channel**: request channels are created as copies of it (inheriting its permissions) |
| Category `720676376335745034` | Primary request-channel category |
| Category `724752007797145620` | Overflow category 1 (used when primary has ≥ 50 channels) |
| Category `725249041822253076` | Overflow category 2 (used when overflow 1 is also full; no further check) |
| Emoji `725149894116900864` `vhLeoFragment` | "Available" choice in `!status` |
| Emoji `725149894154649670` `vhCapricornusFragment` | "Unavailable" choice; also prefixes timeout-log posts |
| Emoji `719953786902413352` `pitfall` | Mod-mail embed title |
| Emoji `724084561469898822` `vhPresent` | Starter-kit embed title |
| Emoji `721122744384880781` `RosieLove` | `!helped` reply |

## 3. Command reference

### 3a. DM commands (member-facing)

When **frozen**, all DM commands reply "The bot is temporarily frozen while being updated." (except for the bypass user).

| Command | Permission | What it actually does |
|---|---|---|
| `!start` (exact match) | Villager role (checked against the guild) | Replies "already submitted" if the user has an active request. Otherwise sends a welcome message with the fandom villager-list link and tells them to use `!villager <name>`. **Creates nothing.** |
| `!villager <name>` | Villager role | Validates the name (case-insensitive exact match against the villager enum). Checks for an **active block**: if one exists, it replies with the block level, days until expiry (see bug B2), and reason. Checks for an existing request. Then shows a ✅/❌ confirm (30 s). On ✅ it **creates a request**: `status=UNACCEPTED`, `available=false`, next position number, timestamp. It then tells the user to use `!status` to mark themselves available. On ❌ it re-sends the villager-list prompt. |
| `!change <name>` | none | Requires an open request. Asks ✅/❌ to change the requested villager, then updates the request. It keeps queue position and **does not check block status or whether the request was already accepted**. |
| `!leave` | none | Requires an open request. On ✅ it **deletes the request outright** (no archive entry). |
| `!status` | none | Requires an open request. Shows a two-emoji menu: Leo = "I have a plot open and am available", Capricornus = "not available". Sets `available`. **Only available requests can be selected by helpers.** |
| `!request` | none | Shows an embed with the user's requested villager, status, submission time, **Online Global Position** (1 + available requests with a lower position), and **Overall Global Position** (1 + all requests with a lower position, including accepted ones). |
| `!modmail <message>` | none (anyone who can DM the bot) | Posts a mention of the user, then an embed (avatar, tag, ID, message, timestamp) to the **mod-mail channel**. Confirms to the user. |
| `!starterkit` | none | ✅/❌ confirm (explains starter kits are for players with ≤ 80 h play time). On ✅ it posts a mention and embed to the **starter-kit channel** and confirms. **No record is stored**, so it can be requested repeatedly. |

### 3b. Guild-channel commands (staff-facing)

Unauthorized or wrong-channel use → the command message is **deleted silently**. When **frozen**, all
guild commands from non-Managers are silently ignored.

| Command | Permission | Channel restriction | What it actually does |
|---|---|---|---|
| `!requests [villager \| sanrio \| filter <villager\|sanrio\|NH8>]` | Helper+ **or** Hiatus | Requests channel | Lists the **next 20** requests that are `UNACCEPTED` and `available`, ordered by position, as "Name - **userId** (**Villager**)". Filter modes: a single villager or the 6 Sanrio villagers ("include"), or `filter X` to **exclude** them. `NH8` excludes 8 named villagers plus Sanrio. **Side effect:** any listed request whose user has left the server is archived as `REMOVED`. It then posts a second embed of **active reservations** (all, single-villager, or multi-villager depending on mode; none in `filter` mode). |
| `!select <userId>` | Helper+ | Requests channel | Selects that user's request (**no availability check**; rejects already-accepted ones). See "Selection workflow" below. |
| `!select <villager>` / `!select <n> <villager>` | Helper+ | Requests channel | Selects the next 1–5 (`n` is clamped to that range) **available** requests for that villager. |
| `!selectnext <n>` | Helper+ | Requests channel | Selects the next `n` **available** requests of any villager **except Sanrio**. `n` isn't clamped (NumUtils defaults to 1 on parse failure). |
| `!close` | Helper+ | Channel whose name starts with `request-` | Archives the request as **`COMPLETED`** (records the closing helper) and **deletes the channel**. If there's no matching request, it posts "user left the queue or the server" to the staff log and deletes the channel. |
| `!close timeout` | Helper+ | `request-` channel | Archives as **`TIMEOUT`**, DMs the user ("you were unavailable… update your status"), and runs the **auto-block check** (see below). It then renders the **last 10 messages as a PNG** and posts it to the timeout-log channel as "requester - villager (Closed by X)", then deletes the channel. Any other argument → the message is deleted and nothing happens. |
| `!reserve <villager>` | Helper+ | Requests channel | Creates a reservation (helper, villager, time). Limit: **max 2 active reservations per helper** and none duplicated. Reservations **expire implicitly after 30 minutes**: queries ignore older ones and they are never cleaned up. |
| `!unreserve <villager>` | Helper+ | Requests channel | Removes the helper's reservation(s) for that villager. |
| `!request <userId \| @mention>` | HelperOrModTeam+ **or** Hiatus | any | The same "Request Information" embed as the DM version, for another user. |
| `!history <userId> [VILLAGER_KEY]` | HelperOrModTeam+ **or** Hiatus | any | Shows the total count of the user's **archived** requests plus the 10 most recent (villager, status, close date, helper). The target must **still be a server member**. The villager filter is matched against the uppercase enum key, so it only works for one-word names. |
| `!queue` | Helper+ or Hiatus | any | Counts requests with `available = true`. |
| `!queue <days>` | Helper+ or Hiatus | any | Counts **all** active requests (any status or availability) submitted within the last `<days>` days. |
| `!queue sanrio` | Helper+ or Hiatus | any | Counts available requests for Sanrio villagers. |
| `!helped [sanrio]` | Helper+ or Hiatus | any | Counts **all archived requests**. Note: this includes REMOVED and TIMEOUT, not just completed ones (see bug B4). |
| `!topvillagers [online]` | Helper+ or Hiatus | any | Top 25 villagers by number of **active** requests (`online` = available only). Ties are broken by name. |
| `!toprequesters` | Helper+ or Hiatus | any | Top 10 users by number of **archived** requests (all statuses), skipping users no longer in the server. Shows name, ID, and count. |
| `!helpers [total \| YYYY \| MM-YYYY]` | **Manager+** | any | Counts **COMPLETED** archived requests per helper. The default is the current month (Chicago time); options are all-time, a year, or a month. Lists every helper still in the server in a single embed field (can exceed Discord's 1024-char field limit). |
| `!statuswipe` | **Manager+** | any | Sets `available = false` on **every** request. |
| `!freeze` | **Manager+** | any | Toggles the in-memory freeze flag. |
| `!block <userId> <reason>` | Helper+ | **#haven** only | Creates a **PERMANENT** block for a user with no block record. If a record already exists it only updates reason, blocker, and timestamp **without changing the level** (bug B1). |
| `!blocks` | Helper+ | #haven only | Lists all non-expired blocks: user, level, date, reason. |
| `!blocks <userId>` | Helper+ | #haven only | Shows one user's block record (level, whether expired, date, reason, blocker). |

### Selection workflow (`!select` / `!selectnext`), per selected request

1. The bot creates a text channel **copied from the template channel**, named `request-<villager>-<4 random hex>`.
   If the primary category has ≥ 50 channels it goes into overflow 1, then overflow 2.
2. It adds permission overrides for the **helper** and the **requester**: View, Read, Send, History. Staff
   (Helper+) also get **Manage Messages** so they can pin.
3. It deletes the helper's reservation for that villager, if any.
4. After 1 s it posts a welcome message pinging the requester and helper: a 30-minute pickup window, a note
   that they may be closed if unresponsive for about 10 minutes, and "don't DM your hunter".
5. It marks the request `ACCEPTED` and stores the helper ID, accepted time, and channel ID.
6. It DMs the requester a link to the new channel. If the DM fails, it is *meant* to post to the staff log (bug B6).
7. It replies in the requests channel with the selected villagers' display names and **internal game IDs**
   (e.g. `brd06`), which helpers presumably use in-game.

### Auto-block on timeout

When a request is closed as `timeout`, the bot counts that user's earlier `TIMEOUT` archives with
`acceptedTimeStamp` within the last **182 hours (about 7.6 days)** *and* after `2021-06-24T22:00Z`. If there are **≥ 2**
(so this is at least the 3rd), it escalates the block:

- no record → **ONE_WEEK**
- ONE_WEEK → **TWO_WEEKS**
- anything else → **PERMANENT**

The escalation happens even if the previous block has expired. It sets reason "Auto-blocked by Villager Bot for timeouts",
clears the blocker, resets the timestamp, and posts a notice in **#haven**.

Block expiry: ONE_WEEK = 7 days and TWO_WEEKS = 14 days after `blockedAt`. PERMANENT never expires. A blocked user
can't create a new request with `!villager`, but other commands aren't blocked.

## 4. Data model (ArangoDB database `villagerhaven`)

| Collection | Key | Fields | Notes |
|---|---|---|---|
| `requests` | `_key` = user ID | `villager` (enum key e.g. `AGENT_S`), `timeStamp`, `status` (`UNACCEPTED`/`ACCEPTED`/`REMOVED`), `acceptedTimeStamp`, `helperUserId`, `channelId`, `available` (bool), `pos` (int) | One active request per user. `pos` comes from `lastpos.json` (a file on the VM) and **must be migrated** (or re-derived as `max(pos)`). |
| `archivedrequests` | auto | `userId`, `villager`, `submissionTimeStamp`, `acceptedTimeStamp`, `archiveTimeStamp`, `helperUserId`, `status` (`COMPLETED`/`REMOVED`/`TIMEOUT`) | History for stats, `!history`, and auto-block. `!leave` does **not** write here. |
| `reservations` | auto | `helperId`, `villager`, `reservedAt` | Rows are never cleaned; "active" means < 30 minutes old. |
| `blocks` | `_key` = user ID | `blockedAt`, `blockLevel` (`ONE_WEEK`/`TWO_WEEKS`/`PERMANENT`), `blockedBy` (nullable), `blockDescription` | One record per user, updated in place. |

Timestamps are Java `Instant`s serialized by the VPack JDK8 module. The AQL comparison against
`'2021-06-24T22:00:00Z'` suggests they're stored as ISO-8601 strings. **Verify this on an export.**

**Villager catalogue:** a ~410-entry Java enum (`obj/Villager.java`). Each entry has a display name, gender, species,
personality, catchphrase, and internal game ID. It includes the 6 Sanrio villagers (added 4/2021) and the
Nov 2021 (2.0) villagers. The stored data references villagers by **enum key**, so the new catalogue must keep
the same keys (or a mapping). Gender, species, personality, and catchphrase are **never used** by any command.

## 5. Bugs & quirks found

Maintainer direction: fix most, but **decide each one case by case**. The recommendation column is a starting
point for that review. "Status" gets filled in as each one is decided.

| # | Behavior | Recommendation | Status |
|---|---|---|---|
| B1 | `!block` on a user who already has a block record never sets it to PERMANENT. It only resets the timer, reason, and blocker. | **Fix.** A manual block should always be permanent, as the command's success message claims. | **Removed**: block feature dropped |
| B2 | `Block.getTimeRemaining()` returns the full block length, not the remaining time, so users are always told "expires in 7 (or 14) days". | **Fix.** Show the real remaining time (a Discord relative timestamp `<t:…:R>` works well). | **Removed**: block feature dropped |
| B3 | The auto-block window is `60*60*26*7` seconds ≈ 7.6 days (probably meant 24 h × 7), plus a hardcoded 2021 cutoff date. | **Fix** to exactly 7 days and make the window configurable. Drop the 2021 cutoff; it's long past. | **Removed**: block feature dropped |
| B4 | `!helped` counts every archived request, including REMOVED and TIMEOUT. | **Fix** to count COMPLETED only, since the message says "requests have been fulfilled". Note the number shown will drop. | **Fix** (agreed) |
| B5 | `!selectnext` without a number builds its usage message but never sends it (missing `.queue()`). | Moot: slash options make the count required or defaulted. | Resolved by rewrite |
| B6 | The DM-failure fallback to the staff log never fires: the `try/catch` wraps an async call. | **Fix.** Catch the REST failure and post to the staff log as intended. | **Fix**: keep sending the DM; if it fails, tell the Haven Hunter in their (ephemeral) command response. |
| B7 | `!select Agent S` (a multi-word villager name without a count) treats "Agent" as a user ID and fails. | Moot: separate typed options (user / villager / count). | Resolved by rewrite |
| B8 | `!requests` archives departed users' requests as a side effect of *listing*. | **Keep the cleanup but move it**, ideally to a `GuildUserRemove` handler or a periodic job, so listing is side-effect free. The handler route needs the privileged `GuildUsers` intent. | **Fix**: periodic background job in the same app, no privileged intent |
| B9 | `!change` doesn't check blocks or whether the request is already accepted. | **Fix** both: blocked users can't change, and accepted requests can't be changed. | **Fix**: `/change` is refused once a Haven Hunter has pulled (accepted) the request. The block half was removed with the block feature. |
| B10 | `!starterkit` has no duplicate protection or record. | **Ask the Events Team.** Recording requests or adding a cooldown is easy if wanted. | **Won't fix**: not a problem in practice |
| B11 | The freeze flag is lost on restart, and the bypass user ID is hardcoded. | **Fix:** persist the flag and let admins (Haven role) bypass instead of a hardcoded user. Or retire `/freeze`. | **Removed**: freeze feature dropped |
| B12 | Overflow category 2 has no capacity check, so channel creation fails when all three categories are full. | **Fix:** use a configurable list of categories, pick the first with space, and give a clear error if all are full. | **Fix**: keep per-request **channels**; categories are created on demand when full and deleted when empty. |
| B13 | `!helpers` output can exceed the embed field limit. | **Fix:** paginate or split across fields. | **Won't fix** as a bug. Put the list in the embed description (4096 chars) instead of a field. |
| B14 | `!history` fails for users who have left the server, even though their archive exists. | **Fix:** take a user ID or user option and show the archive regardless of membership. | **Fix**: accepts a user picker *or* a raw user ID |

## 6. Functionality to migrate: summary

### Core domain (must keep)

1. **Villager request queue:** one active request per member, with a global FIFO position, a villager choice,
   availability status, and block enforcement at creation.
2. **Member self-service:** start/request a villager, change villager, set availability, check queue
   position, leave the queue.
3. **Helper workflow:** list available requests (with filters), reserve and unreserve villagers (2 max, 30-minute
   expiry), select requests by user, villager, or next-N. Selection creates private per-request channels with
   overflow categories, DMs the requester, and reports internal villager IDs.
4. **Request closing:** completed vs timeout, archiving, channel deletion, the requester DM on timeout, and the
   timeout log.
5. **Blocks:** manual permanent block, block lookup and list, automatic escalating blocks for repeated timeouts,
   staff notice.
6. **Staff stats and admin:** queue sizes, total helped, top villagers, top requesters, helper leaderboard by
   period, per-user history, per-user request lookup, status wipe, freeze.
7. **Relays:** mod mail and starter-kit requests forwarded to staff channels.
8. **Data:** all four collections, the position counter, and the villager catalogue with internal IDs.

### What must change because of the rewrite

| Old mechanism | Replacement |
|---|---|
| `!` text commands (DM and guild) | Slash commands. Villager inputs use **autocomplete** (the catalogue is far over the 25-choice limit). |
| DM-based member flow | **Decided: keep it in DMs.** Member commands become slash commands usable in the bot's DMs (`Contexts = [InteractionContextType.BotDMChannel]`, optionally `Guild` too). Implications: (a) they must be registered **globally**, because guild-registered commands never appear in DMs; (b) a DM interaction carries a plain `User`, not the member, so the Villagers-role check needs a REST `GetGuildUserAsync(guildId, userId)` lookup; (c) staff commands stay guild-only (`Contexts = [InteractionContextType.Guild]`). |
| Reaction menus with a 30 s timeout | **Buttons** (confirm/cancel, Available/Unavailable using the same custom emoji). Timeouts must be enforced manually (see the NetCord reference, §4.7). |
| Silent deletion on permission failure | Ephemeral "no permission" replies, plus optional Discord-side command permissions. |
| Hardcoded IDs | Configuration (`appsettings.json` / environment). |
| Channel-name check (`request-` prefix) for `!close` | Look up by the stored `channelId` (more robust), or a button in the request channel. |
| `lastpos.json` counter | A database sequence or max-position query. |
| In-memory freeze flag | A persisted setting. |
| Member cache (`GUILD_MEMBERS` + chunking) | REST lookups per request (volume is small), or keep the privileged `GuildUsers` intent. |

### What will break

- **Timeout transcript image: decided, drop it.** It relied on reading message content, which the bot no longer
  has. `/close timeout` keeps everything else (archive, DM, auto-block check, channel deletion) and may still post
  a text-only line to the timeout-log channel ("requester - villager (Closed by X)"). That's optional.
- **Free-text mod mail:** works fine as a slash command with a text option, or as a **modal** (better for long messages).

### Likely candidates to retire (for your review)

`!start` (it only prints instructions, and slash-command discovery replaces it), `!freeze` (may be unnecessary with
proper deploys), `Moderator+` (unused), and the `NH8` filter preset (tied to a past event). The "mod team" role no
longer exists, so its tier disappears.

## 7. Decisions log

| Date | Decision |
|---|---|
| 2026-09-28 | Pin NetCord `1.0.0-beta.27` (latest; no stable release exists). |
| 2026-09-28 | Roles confirmed (§1). The mod-team role `724720929728495617` is gone, so drop it. |
| 2026-09-28 | Members keep using the bot **through DMs** (DM-enabled global slash commands). |
| 2026-09-28 | Drop the timeout transcript image. No replacement needed. |
| 2026-09-28 | Migrating away from ArangoDB to a new data structure is fine, but existing data must be carried over. |
| 2026-09-28 | Bugs (§5): fix most, deciding case by case. |
| 2026-09-28 | Keep the **current villager list** as-is. Newer villagers exist but may not be distributable. |
| 2026-09-28 | The removed role `724720929728495617` was an old "mini moderator" role, folded into Moderator. No replacement needed. |
| 2026-09-28 | **Remove the block feature entirely**: `!block`, `!blocks`, block checks on request creation, auto-block on timeouts. The server is quiet enough not to need it. The `blocks` collection doesn't need migrating (keep the raw export for reference). |
| 2026-09-28 | **Remove the freeze feature entirely.** |
| 2026-09-28 | **Avoid all privileged intents.** Departed-user cleanup (B8) becomes a periodic background job in the same app, using per-user `GetGuildUserAsync` lookups. (Listing all guild members over REST requires the privileged Server Members intent, so it's not an option.) |
| 2026-09-28 | ~~Private threads~~ rejected: requesters would need access to the parent channel. **Keep one channel per pulled request**, with **dynamic overflow categories** created when full (50-channel limit) and deleted when empty. |
| 2026-09-28 | Requesting starts from a bot-posted **"Request a villager" button panel in the info channel**. Managing an existing request (availability, change villager, leave, position) happens via a **request card** with buttons, reachable from `/request` in DMs. |
| 2026-09-28 | B6: keep the "your request was pulled" DM; on failure, tell the Haven Hunter in their response. |
| 2026-09-28 | Private threads definitively rejected: the parent channel must not be visible to members at all. |
| 2026-09-28 | **Single `/request [villager]` command:** with a name → use it; without → open a modal to type one (with "Did you mean…" suggestions on a miss). The panel also gets a **My request** button showing the same request card. |
| 2026-09-28 | **No template channel.** Request-channel and category permissions are defined in configuration (the template was accidentally deleted in the past). The permanent main category is validated on startup and recreated if missing. |
| 2026-09-28 | Old template permissions (to reproduce in config): @everyone **deny** View Channel; Moderator **allow** View Channel + Send Messages. The category has the same. Everything else is default. |
| 2026-09-28 | **Remove reservations.** The `reservations` collection isn't migrated. |
| 2026-09-28 | `/queue list` is **public** (shared view for the team). Bulk pulls exclude Sanrio unless a Sanrio villager is chosen explicitly; the pull count limit is 10. |
| 2026-09-28 | The main request category is always assumed to exist; no creation or recreation logic. |
| 2026-09-28 | The new bot is a **brand-new Discord application**. All access (roles, category overwrites, Integrations) gets set up from scratch (command-design §6.3). |
| 2026-09-28 | **PostgreSQL**, one server shared by the bot suite (one database per bot), everything in **Docker Compose** (`deploy/`). |
| 2026-09-29 | Hosting: **OVHcloud VPS-1** (US datacenter, about $4.54–6.46/month). Oracle Always Free was abandoned: the account was stuck provisioning and there was no Ampere capacity. Budget cap $10/month (hosting-options.md, "Decision"). |
| 2026-09-29 | **Sanrio villagers can no longer be distributed** (amiibo-only; the workaround was patched). They're marked `requestable: false` in the villager list: members can't request them or mark an existing Sanrio request available (they can change villager or leave), and the import sets their requests to not available. |
| 2026-09-28 | Import: archive requests not touched in **365 days** (by decoded `_rev`) and the 3 stale pulls as `REMOVED`; renumber queue positions by submission time. `lastpos.json` isn't needed. |
| 2026-09-28 | `/history`-style lookups take a user picker *or* a raw user ID, plus a right-click **Apps → Request History** user command. |
| 2026-09-28 | Use modern interaction features wherever they fit: modals, user/string select menus, buttons, context-menu commands, Components V2 layouts. |
