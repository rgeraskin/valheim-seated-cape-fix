#!/bin/bash
# perf.sh <tag>: probe cost on four seats, frame times sitting on the ground with the fix on and off (Mac, CapeTest world)
T=$1
cd "$(dirname "$0")/../.."
run() { HWAIT=${HWAIT:-120} tools/harness/mac-cmd.sh | grep -E "probetime n|frametimes |perf probes|\] ! |hidden|shown" | sed "s/^\[[0-9:.]*\] /$T $1 /" | cut -c1-200; }
sc() { printf 'stand\nwait 1\n%s\nwait 3\nprobetime 300\n' "${2//;/$'\n'}" | run "$1"; }
printf 'cfg rgeraskin.SeatedCapeFix General.Enabled true\nclearinv\nequip CapeLinen\ntarget local\n' | tools/harness/mac-cmd.sh >/dev/null
sc sled 'tp 112 253 0;settle;tpto WinterSled 10 0.8;use SledSeat;wait 1;perf;perf reset'
sc long 'tp 150 -53 0;settle;helm 40'
sc karve 'tp 121 -34 0;settle;helm 30'
sc bench 'tp 110 253 0;settle;sit bench'
sc ground 'tp 101 258 90;settle;emote sit'
for m in on off on off; do
  v=$([ $m = on ] && echo true || echo false)
  printf 'cfg rgeraskin.SeatedCapeFix General.Enabled %s\nwait 2\nframetimes 15 %s\n' $v $m | run ground
done
printf 'cfg rgeraskin.SeatedCapeFix General.Enabled true\nstand\n' | tools/harness/mac-cmd.sh >/dev/null
