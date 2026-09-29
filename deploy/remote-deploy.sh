#!/usr/bin/env bash
# Runs on the VM: builds the images from the code already copied there, then applies a bot action.
# Called by .github/workflows/deploy.yml; can also be run by hand:
#
#   deploy/remote-deploy.sh leave-as-is        # build only; the bot is not started or restarted
#   deploy/remote-deploy.sh start-or-restart   # build, then (re)start the bot on the new image
#   deploy/remote-deploy.sh stop               # build, then stop the bot
set -euo pipefail

cd "$(dirname "$0")"
action="${1:-leave-as-is}"

case "$action" in
  leave-as-is | start-or-restart | stop) ;;
  *) echo "Unknown action '$action' (expected leave-as-is, start-or-restart or stop)." >&2; exit 2 ;;
esac

if [[ ! -f .env ]]; then
  echo "deploy/.env is missing on the VM. Create it from .env.example first (docs/deployment.md, Phase 4)." >&2
  exit 1
fi

echo "::group::Postgres"
docker compose up -d --wait postgres
echo "::endgroup::"

echo "::group::Build images"
docker compose --profile tools build
echo "::endgroup::"

case "$action" in
  leave-as-is)
    echo "Built. The bot was left as it was: not started, stopped or restarted."
    ;;
  stop)
    docker compose stop villager-bot
    ;;
  start-or-restart)
    docker compose up -d villager-bot
    # The bot has no health endpoint; make sure it's still running after startup (bad token, missing IDs, etc. exit fast).
    sleep 20
    if [[ -z "$(docker compose ps --status running --quiet villager-bot)" ]]; then
      echo "villager-bot is not running after startup. Recent logs:" >&2
      docker compose logs --tail 50 villager-bot >&2
      exit 1
    fi
    echo "::group::Startup logs"
    docker compose logs --tail 30 villager-bot
    echo "::endgroup::"
    ;;
esac

docker image prune -f > /dev/null
docker compose ps
