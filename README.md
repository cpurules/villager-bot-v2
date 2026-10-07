# Villager Bot

Villager Bot is a C#/.NET rewrite of a long-running Discord bot used to coordinate villager requests for an Animal Crossing community. The original bot was written in Java/JDA and depended on message content parsing; this version moves the workflow to Discord interactions and a cleaner, testable .NET codebase.

The project is split into a few focused pieces:

- `src/VillagerBot.Bot`: the NetCord host app and Discord interaction modules.
- `src/VillagerBot.Core`: domain logic and the embedded villager catalog.
- `src/VillagerBot.Data`: EF Core/Postgres persistence.
- `src/VillagerBot.Migration`: one-off tooling for importing legacy ArangoDB exports.
- `tests/VillagerBot.Tests`: unit tests for config validation, access rules, migration helpers, and core behavior.

## What it does

- Runs entirely through slash commands, buttons, select menus, and modals.
- Handles member request flows in DMs.
- Supports staff-side queue management, pull tracking, lookups, and closures.
- Persists state in Postgres through EF Core.
- Includes a migration path for data exported from the legacy bot.

## Tech stack

- .NET 10
- NetCord
- Entity Framework Core + PostgreSQL
- xUnit v3
- Docker Compose for local Postgres and deployment packaging

## Running it locally

1. Start the local database:

```bash
cd deploy
cp .env.example .env
docker compose -f compose.yaml -f compose.dev.yaml up -d --wait postgres
```

2. Provide secrets for the bot project:

```bash
dotnet user-secrets set "Discord:Token" "<your bot token>" --project src/VillagerBot.Bot
dotnet user-secrets set "ConnectionStrings:VillagerBot" "Host=localhost;Database=villager_bot;Username=villager_bot;Password=<password from deploy/.env>;GSS Encryption Mode=Disable" --project src/VillagerBot.Bot
```

3. Build or run:

```bash
dotnet build
dotnet run --project src/VillagerBot.Bot
```

## Tests

```bash
dotnet test
```

## Legacy data import

The migration tool reads JSONL exports from the old ArangoDB-backed bot and plans or applies an import into the new Postgres schema.

Dry run:

```bash
dotnet run --project src/VillagerBot.Migration -- --export data-export
```

The sample export directory in this repo is git-ignored because the real files contain member IDs.

## Notes

- Tokens and database passwords are intentionally not committed.
- This repo is a working rewrite, not a generic Discord bot template.
- The docs folder contains deeper notes on command design, migration, deployment, and NetCord usage.