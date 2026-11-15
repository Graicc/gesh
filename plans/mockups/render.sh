#!/bin/sh
# Renders every mockup to PNG with headless Edge. Usage: sh render.sh [name ...]
EDGE="/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe"
HERE="C:/Users/craig/Documents/gesh/plans/mockups"
PROFILE="${TEMP:-/tmp}/gesh-mockup-profile"
render() { # name width height
  "$EDGE" --headless=new --disable-gpu --hide-scrollbars --no-sandbox --user-data-dir="$PROFILE-$1" --screenshot="$HERE/$1.png" --window-size=$2,$3 "file:///$HERE/mockups.html#$1" >/dev/null 2>&1
}
sizes="panel-edit:880:900 panel-select:880:900 panel-brush:880:1280 panel-fill:880:1280 panel-pick:880:1280 panel-placed:880:1280 home-gallery:1960:1420 home-confirm:1960:1420 home-empty:1960:1420 home-settings:1960:1420 home-info:1960:1420 radial:1100:920 hints:1200:800 fingertips:1560:520 flow:1700:1000 storyboard:2680:1620 scene-palm:1600:1000 scene-hover:1600:1000 scene-radial:1600:1000 scene-home:1600:1000 scene-hint:1600:1000 scene-select:1600:1000"
for s in $sizes; do
  name=${s%%:*}; rest=${s#*:}; w=${rest%%:*}; h=${rest#*:}
  if [ $# -eq 0 ] || echo " $* " | grep -q " $name "; then render $name $w $h; fi
done
rm -rf "$PROFILE"-*
