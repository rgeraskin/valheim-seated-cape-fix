#!/bin/bash
# seatall.sh <tag> <cape>: seattest every seat prefab (land ones in the meadow, ships at sea), Mac
cd "$(dirname "$0")/../.."
LAND="BFP_Bench1 BFP_Bench7 BFP_FineWoodChair1 BFP_FineWoodChair4 BFP_FineWoodStool BFP_LogChair BFP_MiniStool1 BFP_PoolChair BFP_Stool1 dvergr_chair_bal dvergr_stool_bal corewood_stool_bal piece_darkwood_bench_bal piece_elegant_bench_bal piece_elegant_bench2_bal piece_emberwood_stool_bal piece_emberwoodchair_bal piece_sofa_bal piece_chicken_nest_bal rae_HorseChair rae_HorseCart rae_OdinHorse piece_asksvinskeleton piece_bathtub piece_pot3_cracked rug_Bjorn Moose piece_blackmarble_throne piece_chair02"
SEA="BBA_Canoe BBA_CargoRaft BBA_Drakker BBA_Knarr BBA_Kvalsund BBA_OutriggerKarve BBA_OutriggerSkeid BBA_Snekkja MercantShip CargoShip BigCargoShip RowingCanoe DoubleRowingCanoe LittleBoat WarShip OdinsBarrelRide OdinsDuckRide Trailership VikingShip_Ashlands Raft"
{ printf 'waitplayer\ngod\nclearinv\nequip %s\ncfg rgeraskin.SeatedCapeFix General.Enabled true\ntp 101 258 90\nsettle\ntidy 30\n' "$2"
  for s in ${3:-$LAND}; do printf 'tidy 15\nseattest %s %s\n' $s "$SHOT"; done
  printf 'tp 121 -34 0\nsettle\nlook 0 0\ntidy 40\n'
  for s in ${4:-$SEA}; do printf 'tidy 30\nseattest %s %s\n' $s "$SHOT"; done
} | HWAIT=3000 tools/harness/mac-cmd.sh | grep -E "seattest |capeclip |poke |\] ! " | grep -v "> " | sed 's/^\[[0-9:.]*\] //' | sed -E 's/ min .* crossed/ crossed/; s/inLegs=0 fwdUnder=0 //; s/verts=[0-9]+ //' | sed "s/^/$1 /" | cut -c1-230
