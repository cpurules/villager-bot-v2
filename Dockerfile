# Multi-arch build: the SDK stage runs on the build machine's platform and cross-publishes for the target
# (e.g. linux/arm64 for an Oracle Ampere A1 host), so no emulation is needed.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Restore first so package downloads are cached independently of source changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/VillagerBot.Core/VillagerBot.Core.csproj src/VillagerBot.Core/
COPY src/VillagerBot.Data/VillagerBot.Data.csproj src/VillagerBot.Data/
COPY src/VillagerBot.Bot/VillagerBot.Bot.csproj src/VillagerBot.Bot/
COPY src/VillagerBot.Migration/VillagerBot.Migration.csproj src/VillagerBot.Migration/
RUN dotnet restore src/VillagerBot.Bot/VillagerBot.Bot.csproj -a $TARGETARCH \
 && dotnet restore src/VillagerBot.Migration/VillagerBot.Migration.csproj -a $TARGETARCH

COPY src/ src/
RUN dotnet publish src/VillagerBot.Bot/VillagerBot.Bot.csproj -a $TARGETARCH -c Release --no-restore -o /out/bot \
 && dotnet publish src/VillagerBot.Migration/VillagerBot.Migration.csproj -a $TARGETARCH -c Release --no-restore -o /out/migration

# The Debian runtime image includes ICU (needed for accent-insensitive villager name matching) and tzdata.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS bot
WORKDIR /app
COPY --from=build /out/bot .
USER $APP_UID
ENTRYPOINT ["dotnet", "VillagerBot.Bot.dll"]

# One-off legacy import: docker compose run --rm villager-bot-migration --apply
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS migration
WORKDIR /app
COPY --from=build /out/migration .
USER $APP_UID
ENTRYPOINT ["dotnet", "VillagerBot.Migration.dll", "--export", "/data-export"]
