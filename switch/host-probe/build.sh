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
# Opt-in source build only. CI and ordinary build remain gameplay-data read-only.
if [[ "${HOST_N2_EXPERIMENTAL:-0}" == "1" ]]; then
  echo "N2 experimental outgoing plaintext writes ENABLED in this local build" >&2
  sed -i 's/^CXX_FLAGS :=$/CXX_FLAGS := -DRC_N2_ENABLE_WRITE=1/' "${WORK}/config.mk"
fi
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
NSO="${WORK}/deploy/subsdk9"
NPDM="${WORK}/deploy/main.npdm"
[[ -f "${NSO}" && -f "${NPDM}" ]] || {
  echo "Expected exlaunch deploy/subsdk9 + deploy/main.npdm not found." >&2
  find "${WORK}" -maxdepth 3 \( -name '*.nso' -o -name '*.npdm' \) -print
  exit 1
}
cp "${NSO}" "${OUT}/subsdk9"
cp "${NPDM}" "${OUT}/main.npdm"
# Address discovery depends on exact symbols from the SAME compiled binary.
# If the map cannot be generated, fail the build rather than guessing offsets.
test -f "${WORK}/exlaunch.elf" || { echo "Missing unstripped exlaunch.elf" >&2; exit 1; }
python3 "${ROOT}/gen_mailbox_manifest.py" \
  --elf "${WORK}/exlaunch.elf" \
  --out "${OUT}/mailbox-locator.json" \
  --nm "${DEVKITPRO}/devkitA64/bin/aarch64-none-elf-nm"
sha256sum "${OUT}/subsdk9" "${OUT}/main.npdm" "${OUT}/mailbox-locator.json" > "${OUT}/SHA256SUMS.txt"
cat > "${OUT}/INSTALL_NOT_AUTOMATIC.txt" <<EOF
Experimental research-only probe. Not deployed automatically.
Violet Title ID: ${TITLE_ID}
Game version: 4.0.0 (17 opcode fingerprints, including E32984 itself; actual Build ID MUST be checked on console)
Build ID expected: 709bfd66115298640155fcc4979dba151c7cc79a
Atmosphere target: atmosphere/contents/${TITLE_ID}/exefs/subsdk9
Companion NPDM: follow exlaunch's installation guidance; do not replace stock
main.npdm without checking intended loader compatibility.
Control: C# Host Probe window via the existing sys-botbase absolute read/write connection.
Mailbox: place mailbox-locator.json beside RaidCrawler EXE for automated ASLR discovery; manual debug log address remains fallback.
Protocol: RC protocol v3, header 0x40, mailbox 0x800, enable at +0x10.
Batch preview (read-only): request mode 3, 1-28 rows from mailbox+0x620.
Default N2 mode = OFF. Building with HOST_N2_EXPERIMENTAL=1 is required
for outgoing one-row or 1-28 row experimental writes; public CI artifacts are READ-ONLY.
Default: disabled, no SD files, no second TCP listener.
Default CI: no game reward writes. Hook code IS instrumented.
Experimental build may modify local outgoing plaintext; real send/receive/save unverified.
EOF
echo "Build completed; artifacts: ${OUT}"
