# Villager Bot (C# / NetCord rewrite)

Rewrite of the Java/JDA Villager Haven bot (`D:\git\_vh-bots_old\villager-bot\`, which the current maintainer did
not author) as a C# .NET 10 app on NetCord. The rewrite was triggered by the loss of the privileged Message Content intent.

## Required reading

- [docs/netcord-reference.md](docs/netcord-reference.md): NetCord patterns and **how to verify APIs**. Read it before
  writing any NetCord code.
- [docs/legacy-bot-analysis.md](docs/legacy-bot-analysis.md): every legacy command, its permissions, the data model,
  known bugs, and the migration scope.
- [docs/hosting-options.md](docs/hosting-options.md): deployment comparison and recommendation.
- [docs/command-design.md](docs/command-design.md): the new command and interaction surface (reviewed).
  This is the spec to implement against.
- [docs/data-migration.md](docs/data-migration.md): Postgres/EF Core schema, legacy field mapping, export procedure, and cutover.
  Legacy exports live in `data-export/` (git-ignored; contains member IDs).

## Layout

| Path | What |
|---|---|
| `src/VillagerBot.Core` | Domain code with no Discord or DB dependencies: the villager catalogue (`Villagers/villagers.json`, embedded; generated from the legacy enum) |
| `src/VillagerBot.Data` | EF Core entities, `VillagerBotDbContext` (Postgres, snake_case, `ulong`→`bigint`), migrations |
| `src/VillagerBot.Bot` | NetCord Generic Host app, organized by feature: `Access/` (role tiers, `RequireAccess` precondition), `Requests/` (member flow: service, views, modules), `Relays/` (mod mail, starter kits), `Panel/` (info-channel panel, `/admin`), `Ui/` (shared view and markup helpers), `Configuration/` |
| `src/VillagerBot.Migration` | One-off legacy ArangoDB import tool (dry run by default) |
| `tests/VillagerBot.Tests` | xUnit v3 tests (Microsoft.Testing.Platform runner via `global.json`) |
| `deploy/` | Docker Compose for the bot suite: shared Postgres (one DB per bot), per-bot services, `.env.example` |

Package versions are pinned centrally in `Directory.Packages.props`.

## Commands

```bash
dotnet build
dotnet test
dotnet run --project src/VillagerBot.Migration -- --export data-export          # import dry run
dotnet ef migrations add <Name> --project src/VillagerBot.Data                  # schema change (local tool: dotnet tool restore)
cd deploy && docker compose -f compose.yaml -f compose.dev.yaml up -d postgres   # local Postgres on 127.0.0.1:5432
cd deploy && docker compose up -d --build                                        # full stack
```

## Environments (test vs live server)

All Discord IDs are configuration, never code. `DOTNET_ENVIRONMENT` picks the server:

| Environment | IDs from | Used by |
|---|---|---|
| `Production` | `src/VillagerBot.Bot/appsettings.Production.json` (Villager Haven) | Docker default (`VILLAGER_BOT_ENVIRONMENT` in `deploy/.env`) |
| `Test` | `src/VillagerBot.Bot/appsettings.Test.json` (test guild) | IDE launch profile "Test server" |

Startup fails with a list of any missing IDs (`VillagerBotOptionsValidator`); emoji IDs are optional and fall back to
standard emoji. Any value can be overridden by environment variable (`VillagerBot__Channels__Info=...`).
Secrets for local runs: `dotnet user-secrets set "Discord:Token" ...` and `"ConnectionStrings:VillagerBot" ...` in
`src/VillagerBot.Bot` (loaded in every environment). Note `dotnet run` applies the first launch profile unless given
`--no-launch-profile`.

## Rules

- **Never assume a NetCord API exists.** Verify types, members, and overloads against <https://netcord.dev/docs/>
  (fastest: grep `https://netcord.dev/xrefmap.yml`). NetCord is pre-1.0 beta and changes frequently.
- Pin exact NetCord package versions. Currently **`1.0.0-beta.27`** for all NetCord packages. There is no stable
  release, so upgrade deliberately.
- Member commands run in the **bot's DMs**, so commands are registered globally with per-command `Contexts`
  (see netcord-reference §4.12). Staff commands are guild-only.
- Decisions made so far are logged in legacy-bot-analysis §7. Bugs are fixed case by case (§5 status column).
- Interactions only (slash commands, buttons, select menus, modals). **No text/prefix commands and no
  `MessageContent` intent.**
- No hardcoded Discord IDs. Guild, channel, category, role, and emoji IDs come from configuration.
- Secrets (bot token, DB credentials) come from environment variables or user secrets, never committed files.
