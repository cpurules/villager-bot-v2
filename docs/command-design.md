# Command & Interaction Design

Design for the rewrite's user-facing surface, built from the decisions in
[legacy-bot-analysis.md §7](legacy-bot-analysis.md#7-decisions-log). NetCord mechanics are in
[netcord-reference.md](netcord-reference.md). Status: **reviewed** (2026-09-28). Sections 3–6 approved with the changes noted in §10.

## 1. Principles

- **Interactions only:** slash commands, buttons, select menus, modals, and user (right-click) commands. No
  message-content reading and **no privileged intents**.
- **Private by default:** responses are ephemeral unless something is deliberately public (e.g. a request channel's
  welcome message).
- **Global registration + per-command contexts:** member commands work in the bot's DMs *and* the server; staff
  commands are server-only.
- **Nothing hardcoded:** guild, channel, category, role, and emoji IDs, villager groups, and permission templates all live in
  configuration (§8).
- **Every click is re-validated.** Buttons don't expire, so handlers re-check current state and that the clicker is
  allowed before acting.
- **Times are shown as Discord timestamps** (`<t:unix:f>` / `<t:unix:R>`), so each viewer sees their own timezone.
  One configured timezone (America/Chicago) is still used for month boundaries in stats.

## 2. Roles & access tiers

| Tier | Roles (config) | Can use |
|---|---|---|
| **Member** | Villagers | Panel, `/request`, the request card, `/modmail`, `/starterkit` |
| **Hunter** | Haven Hunter, Moderator, Haven | All staff commands except admin ones |
| **Hiatus** | Hunter Hiatus | Read-only staff commands (list, lookups, history, stats), no pulling or closing |
| **Admin** | Haven | Everything, including `/stats hunters` and `/admin …` |

`/modmail` and `/starterkit` were open to anyone before. Decided: `/starterkit` requires the Villagers role, and `/modmail`
stays open to any server member.

**Visibility vs enforcement.** Staff commands are registered with `DefaultGuildPermissions` = none, which makes them
admin-only by default and hides them from ordinary members. The server's admins then grant the Hunter and Hiatus roles
access in *Server Settings → Integrations → Villager Bot*. The bot **also** enforces tiers in code with a role-check
precondition, so a misconfigured Integrations setting can't open anything up. (Verify that NetCord emits `"0"` for empty
permissions.)

## 3. Member experience

### 3.1 The panel (info channel)

A bot-authored message in the info channel, posted or refreshed with `/admin panel`. The bot stores its message ID and
edits the same message on refresh.

> **Villager requests**
> Short instructions text (configurable)
> [ 🏝️ Request a villager ] [ 📋 My request ] [ 📖 Villager list ↗ ] *(link button to the fandom list)*

- **Request a villager** starts the flow in §3.2, or shows the card if they already have a request.
- **My request** shows the card (§3.3), or says "You don't have a request. Want to make one?" with a button.

Both reply **ephemerally in the info channel**. In the server, the interaction includes the member's roles, so the Member
check needs no extra lookup.

### 3.2 `/request [villager]` (DM + server)

One command for everything. The `villager` option has type-ahead suggestions drawn from the catalogue.

| State | `villager` given | Result |
|---|---|---|
| No active request | no | **Modal**: "Which villager?" (short text input) |
| No active request | yes | Confirm: "Request **Raymond**?" ✅ Confirm / ❌ Cancel |
| Active, not pulled | no | Show the **request card** |
| Active, not pulled | yes | Confirm: "Change your request from **Audie** to **Raymond**?" |
| Active, **pulled** | either | Card in read-only form: "A Haven Hunter is helping you in #request-raymond-3f2a" |

- **Eligibility:** the Villagers role. In DMs this means a REST member lookup, and anyone no longer in the server is refused.
- **Name resolution:** first a case-insensitive exact match on the display name. If that fails, the bot replies "Couldn't
  find *Raymon*. Did you mean…" with up to 5 buttons for the closest matches (prefix, then contains, then edit
  distance), plus **Try again**, which re-opens the modal.
- **On confirm:** create the request (unavailable, next queue position). The reply becomes the request card, with a hint
  to set availability.
- The modal must be the command's **first** response (within 3 s). The "do they already have a request?" lookup is a
  single indexed query, well within that.

### 3.3 The request card

Ephemeral. Reachable from `/request` (DM or server) and the panel's **My request** button.

> **Your villager request**
> Villager: **Raymond** · Status: 🟢 Available *(custom Leo/Capricornus emoji)*
> Submitted: `<t:…:f>` · Available-queue position: **4** · Overall position: **12**
> [ I'm available ] [ I'm not available ] [ Change villager ] [ Leave queue ]

- **Availability buttons:** the button for the current state is disabled. A click updates the request and re-renders
  the card in place.
- **Change villager** opens the same modal and name resolution as §3.2, then asks for confirmation. It's refused once
  the request is pulled (B9).
- **Leave queue** asks "Are you sure?" (✅/❌), then deletes the request. As today, this writes **no** archive entry
  (decided).
- Positions are calculated live when the card renders, so the old 1-second cache is gone.
- Every button carries the owner's user ID in its custom ID, and the handler refuses clicks from anyone else.
  Ephemeral messages already prevent that, but check anyway.

### 3.4 `/modmail` (DM + server)

Opens a **modal** with a paragraph text input (up to 4000 chars). On submit, the bot posts an embed to the mod-mail
channel (user mention, tag, ID, avatar, message, timestamp) and confirms privately. A mention of the user sits above the
embed, as today.

### 3.5 `/starterkit` (DM + server)

Shows the ≤ 80 h eligibility text with ✅ Request / ❌ Cancel. On confirm, the bot posts the embed to the starter-kit
channel and confirms. No duplicate protection (B10: not needed).

## 4. Staff experience

All server-only. **Queue browsing and pull announcements are public**, so the team works from a shared view
rather than in silos. Lookups, stats, and the pulling hunter's own detail reply are ephemeral unless noted.

### 4.1 Browsing the queue: `/queue list` (Hunter, Hiatus)

Options: `villager` (autocomplete; the suggestion list also includes groups like **Sanrio**) and `exclude` (bool:
treat the villager or group as an *exclusion* filter).

- **Posted publicly**, and **only usable in the requests channel** (decided), like the old `!requests`. Elsewhere it
  replies privately: "Use this in #requests".
- Shows the **next 20 available, unpulled** requests by position: name, user, villager, waiting since.
- Carries a **"Pull a request…"** select menu listing those same entries. Picking one runs the pull (§5) for that user.
  Because the message is public, the **handler checks the clicker is a Hunter** (Hiatus and others get a private
  refusal). It also re-checks that the entry is still unpulled, since the list goes stale as others pull.
- Departed members are **no longer cleaned up here** (B8). The background job handles that (§7).
- The `NH8` preset is retired. Villager groups live in config (§8), so new presets need no code.

### 4.2 Pulling: `/pull` (Hunter)

| Subcommand | Options | Picks |
|---|---|---|
| `/pull user` | `user` (picker) **or** `user-id` (text) | That user's unpulled request. Availability is **not** required, matching today's behavior. |
| `/pull villager` | `villager` (autocomplete), `count` 1–10 (default 1) | The next *available* requests for that villager, Sanrio included when that's the villager chosen |
| `/pull next` | `count` 1–10 (default 1) | The next *available* requests of any villager, **excluding Sanrio** |

**Rule (decided):** bulk pulls never include Sanrio villagers unless a Sanrio villager was explicitly chosen (e.g.
`/pull villager Marty 5`). The Sanrio list comes from the `VillagerGroups:Sanrio` config.

The flow for each request is in §5. The hunter's ephemeral reply lists the channels created, plus villager names and
**internal game IDs** (e.g. `brd06`). A DM failure is reported here (B6).

### 4.3 Closing a request (Hunter)

- The welcome message in every request channel carries **[ ✅ Completed ] [ ⏱️ Timed out ]** buttons, usable by
  Hunters only.
- `/close outcome:(completed|timeout)` does the same from inside the channel.
- The request is found by the **stored channel ID**, not the channel name.
- Timed out asks for confirmation first, because it DMs the member.
- The close flow is in §5.3.

### 4.4 Lookups (Hunter, Hiatus)

| Command | Notes |
|---|---|
| `/lookup request` `user` \| `user-id` | The request card (read-only) for any user, plus who pulled it and where |
| `/lookup history` `user` \| `user-id` [`villager`] | Total archived requests plus the 10 most recent (villager, outcome, date, hunter). Works for departed users (B14). The villager filter uses autocomplete, fixing the one-word-only limitation. |
| Right-click user → **Apps → Request Info** | Same as `/lookup request` |
| Right-click user → **Apps → Request History** | Same as `/lookup history` |

User-or-ID options: both are optional, and exactly one must be supplied. The handler validates this.

### 4.5 Stats: `/stats` (Hunter, Hiatus; `hunters` is Admin-only)

| Subcommand | Options | Output |
|---|---|---|
| `queue` | `days` (int, optional) | Available count. With `days`: all requests submitted in the last N days. |
| `helped` | `group` (optional) | **COMPLETED** archives only (B4) |
| `top-villagers` | `available-only` (bool) | Top 25 villagers in the active queue |
| `top-requesters` | — | Top 10 by archived requests (current members) |
| `hunters` | `period`: this month (default) / all time / a year / a month, with `year` and `month` options | Completed requests per hunter, listed in the embed description (B13) |

### 4.6 Admin: `/admin` (Admin)

- `/admin panel` — posts or refreshes the info-channel panel.
- `/admin reset-availability` — sets every request to unavailable (old `!statuswipe`), after a confirmation.
- `/admin sync-categories` — optional manual trigger for the overflow-category cleanup in §6.

## 5. Request channel lifecycle

### 5.1 Pull

1. Re-check that the request is still unpulled. Two hunters can race; the loser gets "already pulled by @X".
2. **Choose a category** (§6) and create `request-<villager>-<4 hex>` inside it, copying the category's permissions
   and adding overwrites for the requester and the hunter (§6.2).
3. Mark the request pulled: hunter, time, channel ID.
4. Post the **welcome message**: it pings the requester and hunter (allowed mentions limited to those two), states the
   30-minute pickup window and the ~10-minute unresponsiveness rule and "don't DM your hunter", and carries the close buttons.
5. **DM the requester** a link to the channel. On failure (403 / "cannot send messages to this user"), tell the hunter
   in their reply (B6).
6. **Public note in the requests channel:** "@Hunter pulled **Raymond** for @member" (decided: yes). Allowed mentions
   should be none, so the note doesn't re-ping anyone.

### 5.2 Messages on pull (unchanged wording, lightly tidied)

The welcome text and DM text carry over from the old bot and will be configurable strings.

### 5.3 Close

| Outcome | Archive status | Member DM | Log | Channel |
|---|---|---|---|---|
| Completed | `COMPLETED` (credited to the **pulling** hunter, as in the legacy data, so stats stay consistent) | — | — | Deleted |
| Timed out | `TIMEOUT` | "You were unavailable… you can request again; please keep your availability updated" | Text line in the timeout-log channel: "member — villager (closed by X)" (decided: yes) | Deleted |

After deleting, if the channel's category is a **dynamic** overflow category that is now empty, delete it (§6).
The transcript image and auto-blocking are **gone** (decisions log).

## 6. Categories & permissions

### 6.1 Category management

- **Main category:** permanent, with its ID in config, and **always assumed to exist** (decided). The bot never creates
  or recreates it. If it's missing or inaccessible, pulls fail with a clear error to the hunter, and startup logs
  a loud warning.
- **Overflow categories:** created on demand when every existing category has ≥ 50 channels, named
  "Villager Requests 2", "3", …, and placed after the main one. They're tracked in the database as bot-owned.
- **Deletion:** a bot-owned overflow category is deleted when its last channel closes. Startup and `/admin
  sync-categories` also delete any empty bot-owned ones. The bot never deletes a category it didn't create.
- **Concurrency:** channel creation is serialized in-process, so simultaneous pulls can't both spawn a new category.

### 6.2 Permissions

Decided: the **category is the source of truth** for staff access. No Moderator (or other staff) overwrites are
configured per channel.

- **Creating a request channel:** copy the parent category's *current* overwrites, then add the requester and hunter
  entries below. Discord only auto-inherits a category's permissions when a channel is created with *no* overwrites.
  We need per-member entries, so the bot copies the category's overwrites explicitly. Staff can therefore keep managing
  access by editing the category in Discord.
- **Creating an overflow category:** copy the main category's current overwrites.

Today's main category (confirmed 2026-09-28): @everyone **denied** View Channel; Moderator **allowed** View Channel and
Send Messages. Setup (§6.3) adds one entry for the bot.

Added per request channel:

| Target | Allow | Source |
|---|---|---|
| Requester | View Channel, Send Messages, Read Message History | Old `addUserToChannel` |
| Pulling hunter | View Channel, Send Messages, Read Message History, **Manage Messages** (pinning) | Old `addUserToChannel` for staff |

Haven (admin) is assumed to have Administrator and needs no entry.

### 6.3 Bot setup (brand-new application)

The bot is a **fresh Discord application** with no existing access, so setup needs to cover:

1. **Developer Portal:** create the application and bot; turn **Public Bot off** (only you can add it); leave **all
   privileged intents off**.
2. **Invite** with scopes `bot` + `applications.commands`.
3. **Server-level permissions for the bot's role.** Discord only lets a bot grant permissions in overwrites that it holds
   itself, and top-level categories check guild-level permissions:
   - View Channels, Send Messages, Embed Links, Read Message History
   - **Manage Channels** (create and delete request channels and overflow categories)
   - **Manage Roles** (write permission overwrites)
   - **Manage Messages** (so it can grant the hunter Manage Messages for pinning)
   - Use External Emojis (harmless; covers the custom status emoji in DMs)
4. **Main category overwrite for the bot's role:** allow View Channel, Send Messages, Read Message History, Manage
   Channels, Manage Permissions. This is required because the category denies @everyone View Channel, which also hides it
   from the bot. Overflow categories copy this automatically.
5. **Channel access** in the channels the bot posts to (info `#villager-request`, requests, mod mail, starter kits,
   staff log, timeout log): View + Send + Embed Links wherever those channels restrict @everyone.
6. **Integrations settings:** grant the staff commands (§2) to Haven Hunter, Moderator, Hunter Hiatus (read-only ones), and Haven.

The bot should run a **startup self-check** that it can see and manage the main category and post to each configured
channel, and log exactly what's missing.

## 7. Background jobs (same app, `BackgroundService`)

| Job | Interval (config) | Does |
|---|---|---|
| Departed-member cleanup (B8) | 6 h | For each active request: `GetGuildUserAsync`; on 404, archive as `REMOVED`. If the request was pulled, post "member left" in the staff log and delete its channel (the old `!close` fallback, now proactive). No privileged intent needed. |
| Overflow-category cleanup | on startup (and via `/admin sync-categories`) | Delete empty bot-owned overflow categories |
| Startup self-check | on startup | Verify access to the main category and configured channels (§6.3) |

Lookups are paced gently, since the active queue is small and REST rate limits are handled by NetCord.

## 8. Configuration outline

```jsonc
{
  "Discord": { "Token": "(secret)" },
  "VillagerBot": {
    "GuildId": 719579184023863378,
    "Roles": { "Villagers": 719738444498337855, "HavenHunter": 720682820812538067,
               "Moderator": 719737002878042134, "Haven": 761041280355532820, "HunterHiatus": 765007555113648148 },
    "Channels": { "Info": 736846420098809959, "Requests": 900848547845537833, "ModMail": 765984666531659806,
                  "StarterKits": 795365351348764702, "StaffLog": 719902403058466818, "TimeoutLog": 725528965347016725 },
    "Categories": { "Main": 720676376335745034, "OverflowNameFormat": "Villager Requests {0}", "MaxChannels": 50 },
    "Emoji": { "Available": 725149894116900864, "Unavailable": 725149894154649670 /* … */ },
    "VillagerGroups": { "Sanrio": ["TOBY", "MARTY", "ETOILE", "CHELSEA", "CHAI", "RILLA"] },
    "StatsTimeZone": "America/Chicago",
    "Jobs": { "DepartedCleanupHours": 6 },
    "Pull": { "MaxCount": 10 }
  }
}
```

`Channels.Info` is new: `#villager-request`, which holds the panel. The old overflow categories `724752007797145620` and
`725249041822253076` are **not** carried over. The bot manages overflow itself.

## 9. Removed features

`!start` (replaced by the panel and `/request`), `!freeze`, `!block`/`!blocks` and auto-blocking, block checks, the
timeout transcript image, the `NH8` preset, the mod-team role tier, the template channel, **reservations**
(`!reserve`/`!unreserve` and the reservations listing), the `/stats queue group:` option, and any main-category
creation logic.

## 9a. Implementation notes (staff side, 2026-09-29)

- **Pull concurrency:** each request is claimed with a conditional `UPDATE … WHERE pulled_at IS NULL` before its channel is
  created; if channel creation fails, the claim is released so the request returns to the queue. Bulk pulls stop at the
  first channel failure rather than repeating it for every candidate.
- **Departed members at pull time:** a pull checks membership first; members who left are archived as `Removed` and
  reported to the hunter (this complements the periodic cleanup job).
- **The queue list** shows members as mentions plus IDs (there's no member cache without a privileged intent). The
  "Pull a request…" menu labels entries by number and villager.
- **Request channels** copy the parent category's overwrites and add the requester, the hunter, and the bot itself (so
  the bot can always post in and delete its own channels). Overflow categories copy the main category's overwrites.
- **Configurable texts:** `VillagerBot:RequestChannel:Welcome`, `PulledDm`, and `TimeoutDm`, with `{member}`, `{hunter}`,
  `{villager}`, `{channel}` placeholders. `VillagerBot:Pull:BulkExcludedGroup` (default `Sanrio`).

## 10. Open questions

None.

### Resolved (2026-09-28)

- `/queue list` is restricted to the requests channel.
- Sections 3–6 reviewed and approved with changes: public `/queue list`; bulk pulls exclude Sanrio unless one is
  chosen explicitly; pull count up to 10; reservations removed; `queue group:` removed; main category always assumed
  to exist; setup assumes a brand-new bot application (§6.3).
- Info channel: `#villager-request` (`736846420098809959`).
- Request-channel staff access comes from the category's permissions (§6.2).
- Public "pulled" note in the requests channel: yes.
- Leave queue: delete the record, no archive.
- `/starterkit`: Villagers only.
- Timeout-log text line: yes.
