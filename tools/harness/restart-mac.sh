#!/bin/bash
# restart-mac.sh: quit the harness game (if ours is running) and start it again with the current builds
cd "$(dirname "$0")/../.."
if pgrep -f 'MacOS/Valheim' >/dev/null; then tools/harness/mac-cmd.sh <<< 'quit' | tail -1; for i in $(seq 60); do pgrep -f 'MacOS/Valheim' >/dev/null || break; sleep 3; done; fi
VH_CHAR=${VH_CHAR:-CapeTester} VH_WORLD=${VH_WORLD:-CapeTest} tools/harness/mac-game.sh ${EXTRA:-.harness/extra/*.dll} 2>&1 | tail -1
