#!/usr/bin/env bash
set -euo pipefail

API_BASE_URL="${API_BASE_URL:-http://localhost:5028}"
WORKDIR="$(mktemp -d)"
FILE_PATH="$WORKDIR/hundred-mb.bin"
SIZE_BYTES=$((100 * 1024 * 1024))

cleanup() {
  rm -rf "$WORKDIR"
}
trap cleanup EXIT

echo "== Generating a $SIZE_BYTES-byte file at $FILE_PATH =="
python3 - "$FILE_PATH" "$SIZE_BYTES" <<'PYEOF'
import os, sys
path, size = sys.argv[1], int(sys.argv[2])
with open(path, "wb") as f:
    remaining = size
    chunk = os.urandom(1024 * 1024)
    while remaining > 0:
        n = min(len(chunk), remaining)
        f.write(chunk[:n])
        remaining -= n
print("done")
PYEOF

LOCAL_SHA256=$(sha256sum "$FILE_PATH" | awk '{print $1}')
echo "Local SHA-256: $LOCAL_SHA256"

echo "== Requesting a mock user token =="
TOKEN=$(curl -sf -X POST "$API_BASE_URL/api/auth/mock-token" \
  -H "Content-Type: application/json" \
  -d '{"role":"user"}' | python3 -c "import sys,json;print(json.load(sys.stdin)['accessToken'])")

echo "== Uploading (timed) =="
START=$(date +%s.%N)
UPLOAD_RESPONSE=$(curl -sf -X POST "$API_BASE_URL/api/files" \
  -H "Authorization: Bearer $TOKEN" \
  -F "tags=streaming-test" \
  -F "file=@$FILE_PATH;type=application/octet-stream")
END=$(date +%s.%N)
ELAPSED=$(python3 -c "print(f'{$END - $START:.2f}')")
echo "Upload took ${ELAPSED}s"

FILE_ID=$(echo "$UPLOAD_RESPONSE" | python3 -c "import sys,json;print(json.load(sys.stdin)['id'])")
SERVER_SIZE=$(echo "$UPLOAD_RESPONSE" | python3 -c "import sys,json;print(json.load(sys.stdin)['sizeBytes'])")
SERVER_CHECKSUM=$(echo "$UPLOAD_RESPONSE" | python3 -c "import sys,json;print(json.load(sys.stdin)['checksum'])")

echo "Server-reported size: $SERVER_SIZE bytes (expected $SIZE_BYTES)"
echo "Server-reported checksum: $SERVER_CHECKSUM"

if [ "$SERVER_SIZE" != "$SIZE_BYTES" ]; then
  echo "FAIL: size mismatch"; exit 1
fi
if [ "$SERVER_CHECKSUM" != "$LOCAL_SHA256" ]; then
  echo "FAIL: checksum mismatch"; exit 1
fi

echo "== Downloading back and verifying byte-for-byte match =="
DOWNLOAD_PATH="$WORKDIR/downloaded.bin"
curl -sf "$API_BASE_URL/api/files/$FILE_ID/download" -H "Authorization: Bearer $TOKEN" -o "$DOWNLOAD_PATH"
DOWNLOAD_SHA256=$(sha256sum "$DOWNLOAD_PATH" | awk '{print $1}')

if [ "$DOWNLOAD_SHA256" != "$LOCAL_SHA256" ]; then
  echo "FAIL: downloaded content does not match uploaded content"; exit 1
fi

echo "== Cleaning up: soft + hard deleting the test file =="
curl -sf -X DELETE "$API_BASE_URL/api/files/$FILE_ID" -H "Authorization: Bearer $TOKEN" -o /dev/null
ADMIN_TOKEN=$(curl -sf -X POST "$API_BASE_URL/api/auth/mock-token" \
  -H "Content-Type: application/json" -d '{"role":"admin"}' | python3 -c "import sys,json;print(json.load(sys.stdin)['accessToken'])")
curl -sf -X DELETE "$API_BASE_URL/api/files/$FILE_ID/hard" -H "Authorization: Bearer $ADMIN_TOKEN" -o /dev/null

echo ""
echo "PASS: 100 MB upload/download verified. Size, checksum, and downloaded bytes all match."
echo "Upload duration: ${ELAPSED}s"
