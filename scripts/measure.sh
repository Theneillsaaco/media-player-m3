#!/usr/bin/env bash
# Baseline for 3A: RSS and CPU of the running player.
# Usage: scripts/measure.sh [seconds]   (default 10). Run it idle, then again while playing.
set -euo pipefail
secs="${1:-10}"
pid="$(pgrep -n -f 'Player.App' || true)"
[ -n "$pid" ] || { echo "Player.App is not running"; exit 1; }

ticks="$(getconf CLK_TCK)"
cpu() { awk '{print $14 + $15}' "/proc/$pid/stat"; }

before="$(cpu)"
sleep "$secs"
after="$(cpu)"

rss_kb="$(awk '/VmRSS/ {print $2}' "/proc/$pid/status")"
threads="$(awk '/Threads/ {print $2}' "/proc/$pid/status")"
cpu_pct="$(awk -v a="$before" -v b="$after" -v t="$ticks" -v s="$secs" 'BEGIN {printf "%.2f", (b - a) / t / s * 100}')"

echo "pid=$pid  rss=$((rss_kb / 1024)) MB  threads=$threads  cpu=${cpu_pct}% (avg over ${secs}s)"

# anon  = memoria "de verdad" del proceso: montones de .NET, Qt, mpv, texturas que el driver guarda en RAM
# file  = bibliotecas y archivos mapeados (se puede descartar si hace falta)
# shmem = memoria compartida (en Linux la usan mucho los drivers de gráficos)
# Si "anon" es lo que crece, es un montón (managed o nativo). Para saber cuál, ejecuta la app con PLAYER_MEMPROBE=gc
# y mira las líneas "mem:" de ~/.local/state/player/player.log.
field() { awk -v k="$1:" '$1 == k {print int($2 / 1024)}' "/proc/$pid/status"; }
echo "breakdown: anon=$(field RssAnon) MB  file=$(field RssFile) MB  shmem=$(field RssShmem) MB"

echo "children:"
for child in $(pgrep -P "$pid" || true); do
  name="$(awk '/^Name:/ {print $2}' "/proc/$child/status" 2>/dev/null || echo '?')"
  crss="$(awk '/VmRSS/ {print int($2 / 1024)}' "/proc/$child/status" 2>/dev/null || echo 0)"
  echo "  $child  $name  ${crss} MB"
  for grandchild in $(pgrep -P "$child" || true); do
    gname="$(awk '/^Name:/ {print $2}' "/proc/$grandchild/status" 2>/dev/null || echo '?')"
    grss="$(awk '/VmRSS/ {print int($2 / 1024)}' "/proc/$grandchild/status" 2>/dev/null || echo 0)"
    echo "    $grandchild  $gname  ${grss} MB"
  done
done
