#!/bin/bash
# gallery.sh <prefix> <cape> <scene...>: off/on shots of each scene from a few angles (Mac, CapeTest world, hud off)
P=$1; C=$2; shift 2
cd "$(dirname "$0")/../.."
scene() {
  case $1 in
    karve)  echo 'tp 121 -34 0;settle;tidy 15;look 0 0;spawn Karve 1 6;wait 4;tpto Karve 30 3;settle;helm 15' ;;
    long)   echo 'tp 150 -53 0;settle;tpto VikingShip 100 3;settle;helm 15' ;;
    bench)  echo 'tp 110 253 0;settle;sit bench' ;;
    sled)   echo 'tp 112 253 0;settle;tidy 6 Cart;sled;wait 3;tpto WinterSled 10 0.8;use SledSeat' ;;
    cart)   echo 'tp 118 253 0;settle;tidy 8 Sled;spawn Cart 1 3;wait 3;tpto Cart 30 0.8;use SledSeat' ;;
    ground) echo 'tp 101 258 90;settle;emote sit' ;;
  esac
}
angles() {
  case $1 in
    karve|long) echo '90 5 3.2 0.3|130 5 3.2 0.1|160 0 3.4 0.0' ;;
    sled|cart) echo '90 0 2.6 -0.1|120 3 2.6 -0.05|60 3 2.6 -0.05' ;;
    *) echo '90 5 2.4 0.2|130 10 2.4 0.3|160 15 2.6 0.4' ;;
  esac
}
{ printf 'waitplayer\ngod\nhud off\nclearinv\nequip %s\n' $C
  for s in "$@"; do
    for m in off on; do
      v=$([ $m = on ] && echo true || echo false)
      printf 'stand\nwait 1\ncfg rgeraskin.SeatedCapeFix General.Enabled %s\n' $v
      scene $s | tr ';' '\n'
      printf 'wait 7\ncapeclip %s_%s_%s\n' $s $m $C
      i=0; IFS='|'; for a in $(angles $s); do i=$((i+1)); IFS=' ' read -r y p d h <<< "$a"; printf 'fshot g_%s_%s_%s_%s_%d %s %s %s %s\n' $P $s $C $m $i $y $p $d $h; done; unset IFS
    done
  done
  printf 'stand\ncfg rgeraskin.SeatedCapeFix General.Enabled true\nhud on\n'
} | HWAIT=1500 tools/harness/mac-cmd.sh | grep -E "capeclip |\] ! " | grep -v '> ' | sed 's/^\[[0-9:.]*\] //' | cut -c1-160
