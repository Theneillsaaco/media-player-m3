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
