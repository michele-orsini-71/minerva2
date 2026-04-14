#!/usr/bin/env bash
set -euo pipefail

DB_NAME="minerva_test"
DB_USER="${PGUSER:-$(whoami)}"
DB_HOST="${PGHOST:-localhost}"
DB_PORT="${PGPORT:-5432}"

CONNSTRING="Host=${DB_HOST};Port=${DB_PORT};Database=${DB_NAME};Username=${DB_USER}"

echo "==> Checking PostgreSQL connection..."
if ! pg_isready -h "$DB_HOST" -p "$DB_PORT" -q 2>/dev/null; then
    echo "ERROR: PostgreSQL is not running on ${DB_HOST}:${DB_PORT}"
    echo "Start it with: brew services start postgresql@18"
    exit 1
fi

echo "==> Creating database '${DB_NAME}' (if not exists)..."
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d postgres -tc \
    "SELECT 1 FROM pg_database WHERE datname = '${DB_NAME}'" | grep -q 1 \
    || createdb -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" "$DB_NAME"

echo "==> Enabling pgvector extension..."
psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -c \
    "CREATE EXTENSION IF NOT EXISTS vector;" 2>/dev/null

echo "==> Running integration tests..."
dotnet test tests/Minerva.IntegrationTests \
    --filter Category=Storage \
    -l "console;verbosity=normal" \
    -e "MINERVA_TEST_CONNSTRING=$CONNSTRING" \
    -e "MINERVA_TEST_DISABLE_CLEANUP=1"
