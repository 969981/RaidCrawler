#!/usr/bin/env bash
# Build the RaidCrawler host N1 read-only probe with a pinned exlaunch version.
# Requires devkitPro / devkitA64 / libnx, git, make, python3.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK="${ROOT}/.work/exlaunch"
EXLAUNCH_COMMIT="f9f4b0dd07b68f97958cb9c79228bbca22ca80d5"
TITLE_ID="01008F6008C5E000" # Violet ONLY; Scarlet is NOT fingerprinted.
if [[ -z "${DEVKITPRO:-}" || ! -f "${DEVKITPRO}/libnx/switch_rules" ]]; then
  echo "Missing devkitPro (DEVKITPRO/libnx/switch_rules)." >&2
  exit 2
fi
mkdir -p "${ROOT}/.work"
if [[ ! -d "${WORK}/.git" ]]; then
  git clone https://github.com/shadowninja108/exlaunch.git "${WORK}"
fi
git -C "${WORK}" fetch origin "${EXLAUNCH_COMMIT}"
git -C "${WORK}" checkout --detach "${EXLAUNCH_COMMIT}"
git -C "${WORK}" reset --hard "${EXLAUNCH_COMMIT}"
cp "${ROOT}/source/program/main.cpp" "${WORK}/source/program/main.cpp"
cat > "${WORK}/config.mk" <<'EOF'
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
python3 - "${WORK}/config.json" "${TITLE_ID}" <<'PY'
import json
import pathlib
import sys
p = pathlib.Path(sys.argv[1])
obj = json.loads(p.read_text())
obj["name"] = "RaidCrawler N1 Observe"
obj["title_id"] = "0x" + sys.argv[2].lower()
p.write_text(json.dumps(obj, indent=2) + "\n")
PY
# Deliberately never invokes FTP deployment or copies files to a console.
make -C "${WORK}" -j"${JOBS:-2}" all
OUT="${ROOT}/out"
mkdir -p "${OUT}"
NSO="${WORK}/exlaunch.nso"
NPDM="${WORK}/exlaunch.npdm"
[[ -f "${NSO}" && -f "${NPDM}" ]] || {
  echo "Expected exlaunch.nso + exlaunch.npdm not found." >&2
  find "${WORK}" -maxdepth 3 \( -name '*.nso' -o -name '*.npdm' \) -print
  exit 1
}
cp "${NSO}" "${OUT}/subsdk9"
cp "${NPDM}" "${OUT}/main.npdm"
sha256sum "${OUT}/subsdk9" "${OUT}/main.npdm" > "${OUT}/SHA256SUMS.txt"
cat > "${OUT}/INSTALL_NOT_AUTOMATIC.txt" <<EOF
Experimental research-only probe. Not deployed automatically.
Violet Title ID: ${TITLE_ID}
Game version: 4.0.0 (12 opcode fingerprints, actual Build ID MUST be checked on console)
Build ID expected: 709bfd66115298640155fcc4979dba151c7cc79a
Atmosphere target: atmosphere/contents/${TITLE_ID}/exefs/subsdk9
Companion NPDM: follow exlaunch's installation guidance; do not replace stock
main.npdm without checking intended loader compatibility.
SD opt-in: /raidcrawler/host-probe.enable
Captured data: /raidcrawler/n1/*.source-rewards.bin and *.bootstrap-rewards.bin
No reward or network writes. Gameplay process IS instrumented.
EOF
echo "Build completed; artifacts: ${OUT}"
