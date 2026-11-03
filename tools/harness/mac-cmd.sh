#!/bin/bash
# Send harness commands (stdin) to the game started by mac-game.sh; print the new log lines once they've all run.
D="$(cd "$(dirname "$0")/../.." && pwd)/.harness"
n=$( [ -f "$D/log.txt" ] && wc -l < "$D/log.txt" || echo 0 )
cat > "$D/cmd.tmp" && mv "$D/cmd.tmp" "$D/cmd.txt"
for _ in $(seq 1 "${HWAIT:-300}"); do
  sleep 1
  out=$(tail -n +$((n + 1)) "$D/log.txt" 2>/dev/null)
  if echo "$out" | grep -q -E "\] done$" || ! pgrep -f "MacOS/Valheim" >/dev/null; then echo "$out"; exit 0; fi
done
echo "$out"; echo TIMEOUT; exit 1
