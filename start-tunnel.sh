#!/bin/bash
# Start cloudflared tunnel and auto-update Telegram Mini App menu button URL.
# Usage: ./start-tunnel.sh [port]
#   port — local port to tunnel (default: 5005, API serves both SPA and API)

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENV_FILE="$SCRIPT_DIR/.env"
LOG_FILE="$SCRIPT_DIR/.cloudflared.log"
DEFAULT_PORT=5005
PORT="${1:-$DEFAULT_PORT}"
MENU_BUTTON_TEXT="Открыть"

# --- Resolve cloudflared binary path ---
CF_BIN=""
for candidate in \
    "cloudflared" \
    "/c/Program Files (x86)/cloudflared/cloudflared.exe" \
    "/c/Program Files/cloudflared/cloudflared.exe" \
    "$LOCALAPPDATA/Programs/cloudflared/cloudflared.exe"; do
    if command -v "$candidate" &>/dev/null || [ -f "$candidate" ]; then
        CF_BIN="$candidate"
        break
    fi
done

if [ -z "$CF_BIN" ]; then
    echo "ERROR: cloudflared not found. Install: winget install cloudflare.cloudflared"
    exit 1
fi
echo "Using: $CF_BIN"

# --- Read bot token from .env ---
if [ ! -f "$ENV_FILE" ]; then
    echo "ERROR: .env file not found at $ENV_FILE"
    exit 1
fi

BOT_TOKEN=$(grep -E '^TELEGRAM_BOT_TOKEN=' "$ENV_FILE" | cut -d'=' -f2-)
if [ -z "$BOT_TOKEN" ]; then
    echo "ERROR: TELEGRAM_BOT_TOKEN not found in .env"
    exit 1
fi

WEBHOOK_SECRET=$(grep -E '^TELEGRAM_WEBHOOK_SECRET=' "$ENV_FILE" | cut -d'=' -f2-)
if [ -z "$WEBHOOK_SECRET" ]; then
    echo "WARNING: TELEGRAM_WEBHOOK_SECRET not found in .env — webhook verification will fail"
fi

# --- Kill existing cloudflared if running ---
taskkill //F //IM cloudflared.exe 2>/dev/null && echo "Stopped existing cloudflared" || true
sleep 1

# --- Clear old log and start cloudflared ---
rm -f "$LOG_FILE"
echo "Starting cloudflared tunnel on port $PORT..."
"$CF_BIN" tunnel --url "http://localhost:$PORT" >"$LOG_FILE" 2>&1 &
CF_PID=$!
disown "$CF_PID" 2>/dev/null || true

# --- Wait for tunnel URL to appear in logs ---
echo "Waiting for tunnel URL..."
TUNNEL_URL=""
for i in $(seq 1 30); do
    sleep 1
    if [ -f "$LOG_FILE" ]; then
        TUNNEL_URL=$(grep -oE 'https://[a-zA-Z0-9_-]+\.trycloudflare\.com' "$LOG_FILE" | head -1 || true)
        if [ -n "$TUNNEL_URL" ]; then
            break
        fi
    fi
    if [ "$i" -eq 30 ]; then
        echo "ERROR: Could not get tunnel URL within 30 seconds"
        echo "--- Log output ---"
        cat "$LOG_FILE" 2>/dev/null
        exit 1
    fi
done

echo ""
echo "=== cloudflared tunnel active ==="
echo "Public URL: $TUNNEL_URL"
echo "Local port: $PORT"
echo ""

# --- Update Telegram Menu Button via Python ---
echo "Updating Telegram Mini App menu button..."
RESPONSE=$(python -c "
import urllib.request, json, sys
payload = {'menu_button': {'type': 'web_app', 'text': '$MENU_BUTTON_TEXT', 'web_app': {'url': '$TUNNEL_URL'}}}
data = json.dumps(payload).encode()
req = urllib.request.Request('https://api.telegram.org/bot$BOT_TOKEN/setChatMenuButton', data=data, headers={'Content-Type': 'application/json'})
try:
    r = urllib.request.urlopen(req)
    print(r.read().decode())
except Exception as e:
    print(str(e), file=sys.stderr)
    sys.exit(1)
")

if echo "$RESPONSE" | grep -q '"ok":true'; then
    echo "Menu button updated: $TUNNEL_URL"
else
    echo "WARNING: Failed to update menu button"
    echo "Response: $RESPONSE"
fi

# --- Update .env with new tunnel URL ---
echo "Updating .env with tunnel URL..."
if grep -q '^TELEGRAM_WEBHOOK_BASE_URL=' "$ENV_FILE"; then
    sed -i "s|^TELEGRAM_WEBHOOK_BASE_URL=.*|TELEGRAM_WEBHOOK_BASE_URL=$TUNNEL_URL|" "$ENV_FILE"
else
    echo "TELEGRAM_WEBHOOK_BASE_URL=$TUNNEL_URL" >> "$ENV_FILE"
fi
if grep -q '^TELEGRAM_MINIAPP_URL=' "$ENV_FILE"; then
    sed -i "s|^TELEGRAM_MINIAPP_URL=.*|TELEGRAM_MINIAPP_URL=$TUNNEL_URL|" "$ENV_FILE"
else
    echo "TELEGRAM_MINIAPP_URL=$TUNNEL_URL" >> "$ENV_FILE"
fi
echo ".env updated: TELEGRAM_WEBHOOK_BASE_URL=$TUNNEL_URL, TELEGRAM_MINIAPP_URL=$TUNNEL_URL"

# --- Register webhook with Telegram ---
echo "Registering Telegram webhook..."
WEBHOOK_URL="$TUNNEL_URL/bot/webhook"
WEBHOOK_RESPONSE=$(python -c "
import urllib.request, json, sys
payload = {'url': '$WEBHOOK_URL', 'secret_token': '$WEBHOOK_SECRET'}
data = json.dumps(payload).encode()
req = urllib.request.Request('https://api.telegram.org/bot$BOT_TOKEN/setWebhook', data=data, headers={'Content-Type': 'application/json'})
try:
    r = urllib.request.urlopen(req)
    print(r.read().decode())
except Exception as e:
    print(str(e), file=sys.stderr)
    sys.exit(1)
")

if echo "$WEBHOOK_RESPONSE" | grep -q '"ok":true'; then
    echo "Webhook registered: $WEBHOOK_URL"
else
    echo "WARNING: Failed to register webhook"
    echo "Response: $WEBHOOK_RESPONSE"
fi

echo ""
echo "=== Ready ==="
echo "Mini App: $TUNNEL_URL"
echo "Webhook: $WEBHOOK_URL"
echo ""
echo "Press Ctrl+C to stop tunnel"

# --- Keep script running, forward Ctrl+C to cloudflared ---
cleanup() {
    echo ""
    echo "Stopping cloudflared..."
    taskkill //F //IM cloudflared.exe 2>/dev/null || kill "$CF_PID" 2>/dev/null || true
    rm -f "$LOG_FILE"
    exit 0
}
trap cleanup INT TERM
wait "$CF_PID" 2>/dev/null || true

# If wait returns (process lost), keep alive until Ctrl+C
while tasklist 2>/dev/null | grep -qi cloudflared; do
    sleep 5
done
echo "cloudflared process ended"
