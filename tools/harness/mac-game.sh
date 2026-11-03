#!/bin/bash
# Start the game on this Mac (Steam build, BepInEx under Rosetta) with valheim-dev-harness, this mod and its companion,
# in an isolated profile .harness/profile: the game folder's own BepInEx/plugins are never loaded or touched.
# The harness loads VH_WORLD (default HarnessTest) as VH_CHAR (HarnessTester). Refuses (exit 3) while Valheim runs here.
# Extra DLLs on the command line join the plugins (e.g. Jotunn + WinterSled to sit on a sled). Window: VH_W x VH_H.
# Then: tools/harness/mac-cmd.sh <<< 'state'
set -euo pipefail
R=$(cd "$(dirname "$0")/../.." && pwd)
H="${VALHEIM_DEV_HARNESS:-$R/../valheim-dev-harness}"
G="${VALHEIM_GAME_DIR:-$HOME/Library/Application Support/Steam/steamapps/common/Valheim}"
D="$R/.harness"
P="$D/profile/BepInEx"
pgrep -f "MacOS/Valheim" >/dev/null && { echo "BUSY: Valheim is already running on this Mac: not starting another" >&2; exit 3; }
mkdir -p "$P/core" "$P/config" "$P/plugins"
rsync -a "$G/BepInEx/core/" "$P/core/"
[ -f "$P/config/BepInEx.cfg" ] || cp "$G/BepInEx/config/BepInEx.cfg" "$P/config/"
cp "$H/DevHarness/bin/Release/ValheimDevHarness.dll" \
   "$R/SeatedCapeFix/bin/Release/SeatedCapeFix.dll" \
   "$R/tools/DevHarness/bin/Release/SeatedCapeFixHarness.dll" "$@" "$P/plugins/"
# a command the last game never read must not reach this one
rm -f "$D/cmd.txt"
n=$( [ -f "$D/log.txt" ] && wc -l < "$D/log.txt" || echo 0 )
cd "$G"
VH_HARNESS_DIR="$D" nohup /usr/bin/arch -x86_64 /bin/bash ./start_game_bepinex.sh --doorstop-target-assembly "$P/core/BepInEx.Preloader.dll" \
  -console -screen-fullscreen 0 -screen-width "${VH_W:-1920}" -screen-height "${VH_H:-1080}" > "$D/game.out" 2>&1 &
for _ in $(seq 1 100); do
  sleep 3
  out=$(tail -n +$((n + 1)) "$D/log.txt" 2>/dev/null || true)
  echo "$out" | grep -q "starting world\|joining\|! " && { echo "$out"; exit 0; }
  pgrep -f "MacOS/Valheim" >/dev/null || { echo "the game exited"; tail -20 "$D/game.out"; exit 1; }
done
echo "TIMEOUT waiting for the world"; exit 1
