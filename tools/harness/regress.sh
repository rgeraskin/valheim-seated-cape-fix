#!/bin/bash
# regress.sh <tag>: every seat with the linen and the feather cape: capeclip + poke (Mac, CapeTest)
cd "$(dirname "$0")/../.."
for C in CapeLinen CapeFeather; do
  { printf 'clearinv\nequip %s\ncfg rgeraskin.SeatedCapeFix General.Enabled true\n' $C
    while IFS='|' read -r name cmds; do printf 'stand\nwait 1\n'; echo "$cmds" | tr ';' '\n'; printf 'wait 6\ncapeclip %s_%s\npoke\n' $name $C; done <<'EOS'
sled|tp 112 253 0;settle;tpto WinterSled 10 0.8;use SledSeat
cart|tp 118 253 0;settle;tidy 8 Sled;spawn Cart 1 3;wait 3;tpto Cart 30 0.8;use SledSeat
bench|tp 110 253 0;settle;sit bench
ground|tp 101 258 90;settle;emote sit
long|tp 150 -53 0;settle;tpto VikingShip 100 3;settle;helm 15
karve|tp 121 -34 0;settle;tpto Karve 100 3;settle;helm 15
EOS
    printf 'stand\n'; } | ${CMD:-env HWAIT=500 tools/harness/mac-cmd.sh} | grep -E "capeclip |poke |\] ! " | grep -v "> " | sed 's/^\[[0-9:.]*\] //' | sed -E 's/ min .* crossed/ crossed/; s/inLegs=0 fwdUnder=0 //; s/verts=[0-9]+ //' | sed "s/^/$1 /" | cut -c1-220
done
