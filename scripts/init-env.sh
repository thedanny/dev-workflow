#!/usr/bin/env sh
# Creates .env for compose.yaml with a random database password and one API key per role.
# The server only ever sees the key hashes; the raw keys are printed once, here.
# Usage: scripts/init-env.sh [--force]
set -eu

cd "$(dirname "$0")/.."

if [ -e .env ] && [ "${1:-}" != "--force" ]; then
  echo ".env already exists. Pass --force to replace it, which invalidates the current keys." >&2
  exit 1
fi

random_hex() { openssl rand -hex 32; }
sha256_hex() { printf %s "$1" | openssl dgst -sha256 | awk '{print $NF}'; }

listener=$(random_hex)
evaluator=$(random_hex)
approver=$(random_hex)
executor=$(random_hex)

umask 077
tr -d '\r' < .env.example | sed \
  -e "s/^POSTGRES_PASSWORD=.*/POSTGRES_PASSWORD=$(random_hex)/" \
  -e "s/^LISTENER_KEY_HASH=.*/LISTENER_KEY_HASH=$(sha256_hex "$listener")/" \
  -e "s/^EVALUATOR_KEY_HASH=.*/EVALUATOR_KEY_HASH=$(sha256_hex "$evaluator")/" \
  -e "s/^APPROVER_KEY_HASH=.*/APPROVER_KEY_HASH=$(sha256_hex "$approver")/" \
  -e "s/^EXECUTOR_KEY_HASH=.*/EXECUTOR_KEY_HASH=$(sha256_hex "$executor")/" \
  > .env

cat <<EOF
Wrote .env. Store these API keys somewhere safe; they are not saved anywhere:

  listener   $listener
  evaluator  $evaluator
  approver   $approver
  executor   $executor
EOF
