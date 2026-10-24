#!/usr/bin/env bash
# Read-only Violet 4.0.0 subsdk9 build. No automatic console deployment.
set -eo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
WORK="$ROOT/.work/exlaunch"
EXLAUNCH_COMMIT="f9f4b0dd07b68f97958cb9c79228bbca22ca80d5"
TITLE_ID="01008F6008C5E000"
if [[ -z "$DEVKITPRO" || ! -f "$DEVKITPRO/libnx/switch_rules" ]]; then
  echo "devkitPro/devkitA64/libnx required; missing DEVKITPRO/libnx/switch_rules" >&2
  exit 2
fi
mkdir -p "$ROOT/.work"
if [[ ! -d "$WORK/.git" ]]; then
  git clone https://github.com/shadowninja108/exlaunch.git "$WORK"
fi
git -C "$WORK" fetch origin "$EXLAUNCH_COMMIT"
git -C "$WORK" checkout --detach "$EXLAUNCH_COMMIT"
git -C "$WORK" reset --hard "$EXLAUNCH_COMMIT"

# A new implementation from main: does not import host v3 binary or write paths.
cp "$ROOT/source/main.cpp" "$WORK/source/program/main.cpp"
cp "$ROOT/include/raid_mailbox_v4.hpp" "$WORK/source/program/raid_mailbox_v4.hpp"
cat > "$WORK/config.mk" <<'EOF'
LOAD_KIND := Module
PROGRAM_ID := 01008F6008C5E000
ELF_EXTRACT :=
PYTHON := python3
NPDM_JSON := config.json
C_FLAGS :=
CXX_FLAGS :=
MOUNT_PATH :=
FTP_IP :=
FTP_PORT :=
FTP_USERNAME :=
FTP_PASSWORD :=
RYU_PATH :=
EOF
python3 - "$WORK/config.json" "$TITLE_ID" <<'PY'
import json
from pathlib import Path
import sys
p = Path(sys.argv[1])
obj = json.loads(p.read_text())
obj["name"] = "RaidCrawler V4 N1 ReadOnly"
obj["title_id"] = "0x" + sys.argv[2].lower()
p.write_text(json.dumps(obj, indent=2) + "\n")
PY

# All builds are READ ONLY; no env switch to enable game-data writes.
make -C "$WORK" -j"$(printenv JOBS || echo 2)" all
OUT="$ROOT/out"
mkdir -p "$OUT"
NSO="$WORK/deploy/subsdk9"
NPDM="$WORK/deploy/main.npdm"
test -s "$NSO"
test -s "$NPDM"
test -s "$WORK/exlaunch.elf"
cp "$NSO" "$OUT/subsdk9"
cp "$NPDM" "$OUT/main.npdm"
python3 "$ROOT/gen_manifest.py" \
  --elf "$WORK/exlaunch.elf" \
  --nm "$DEVKITPRO/devkitA64/bin/aarch64-none-elf-nm" \
  --out "$OUT/mailbox-v4-locator.json"
(
 cd "$OUT"
 sha256sum subsdk9 main.npdm mailbox-v4-locator.json > SHA256SUMS.txt
)
cat > "$OUT/README-INSTALL.txt" <<EOF
RaidCrawler V4 N1 READ-ONLY subsdk9 for Violet 4.0.0.
Not tested on real Switch; this does NOT write reward values.
Title ID $TITLE_ID; Build ID expected 709bfd66115298640155fcc4979dba151c7cc79a.
Only 17 code-word guard checks run on device: compare actual Build ID separately!
Research install path: atmosphere/contents/$TITLE_ID/exefs/subsdk9
Existing subsdk9/main.npdm might conflict: DO NOT overwrite blindly.
mailbox-v4-locator.json MUST be paired with this exact subsdk9 binary.
No automated SD/FTP deployment. Only user-initiated local tests.
N1 hook main+0xE32984 reads RaidPoint and profile; V4 capability=1.
EOF
echo "V4 readonly output: $OUT"
