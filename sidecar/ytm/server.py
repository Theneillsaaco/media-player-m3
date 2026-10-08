#!/usr/bin/env python3
"""YouTube Music sidecar: one JSON request per line on stdin, one JSON reply per line on stdout.

  request: {"id": 1, "method": "search", "params": {"query": "...", "limit": 30}}
  reply:   {"id": 1, "ok": true,  "result": ...}
           {"id": 1, "ok": false, "error": "..."}

Methods: ping, search (songs only), song (metadata of one videoId).
No authentication in phase 1. Stream resolution is left to mpv's yt-dlp hook.

Result normalisation (artwork upscaling, artist/album extraction) follows Sung's
helper/catalog.py (MIT), rewritten for this protocol.
"""
import json
import re
import sys

# Protocol output goes to the real stdout; anything a library prints goes to stderr.
_out = sys.stdout
sys.stdout = sys.stderr
for _s in (sys.stdin, _out):
    try:
        _s.reconfigure(encoding="utf-8")
    except Exception:
        pass

VIDEO_ID = re.compile(r"[A-Za-z0-9_-]{11}")
_api = None


def _with_timeout(original, timeout):
    def request(*args, **kwargs):
        kwargs.setdefault("timeout", timeout)
        return original(*args, **kwargs)
    return request


def api():
    global _api
    if _api is None:
        from ytmusicapi import YTMusic
        client = YTMusic(requests_session=True)
        client._session.request = _with_timeout(client._session.request, 20)
        _api = client
    return _api


def artwork(item):
    # Search results carry `thumbnails`; watch playlists carry `thumbnail`.
    thumbs = item.get("thumbnails") or item.get("thumbnail") or []
    if not thumbs:
        return ""
    src = thumbs[-1].get("url", "")
    # Google serves tiny thumbnails; ask the same image service for 544px.
    if "googleusercontent.com/" in src or "ggpht.com/" in src:
        src = re.sub(r"=w\d+-h\d+[^?]*$", "=w544-h544-l90-rj", src)
    return src


def names(value):
    if isinstance(value, str):
        return value
    return ", ".join(a.get("name", "") for a in (value or []) if isinstance(a, dict))


def track(item):
    video = item.get("videoId") or ""
    if not video:
        return None
    album = item.get("album") or {}
    if isinstance(album, str):
        album = {"name": album}
    return {
        "id": video,
        "title": item.get("title") or "Untitled",
        "artist": names(item.get("artists")),
        "album": album.get("name", "") if isinstance(album, dict) else "",
        "art": artwork(item),
        "seconds": item.get("duration_seconds") or 0,
        "kind": "song",
    }


def search(params):
    query = str(params.get("query", "")).strip()
    if not query:
        return []
    limit = min(max(int(params.get("limit", 30)), 1), 100)
    found = api().search(query, filter="songs", limit=limit)
    return [t for t in (track(i) for i in found if isinstance(i, dict)) if t]


def song(params):
    video = str(params.get("id", ""))
    if not VIDEO_ID.fullmatch(video):
        raise ValueError("Invalid YouTube video ID")
    details = api().get_song(video).get("videoDetails") or {}
    if not details:
        raise ValueError("This song is unavailable")
    return {
        "id": video,
        "title": details.get("title") or "Untitled",
        "artist": details.get("author", ""),
        "album": "",
        "art": artwork({"thumbnails": details.get("thumbnail", {}).get("thumbnails", [])}),
        "seconds": int(details.get("lengthSeconds") or 0),
        "kind": "song",
    }


METHODS = {"ping": lambda p: "pong", "search": search, "song": song}


def reply(obj):
    _out.write(json.dumps(obj, ensure_ascii=False) + "\n")
    _out.flush()


def main():
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        rid = None
        try:
            req = json.loads(line)
            rid = req.get("id")
            result = METHODS[req["method"]](req.get("params") or {})
            reply({"id": rid, "ok": True, "result": result})
        except Exception as exc:  # one bad request must never kill the process
            reply({"id": rid, "ok": False, "error": (str(exc) or type(exc).__name__)[-500:]})


if __name__ == "__main__":
    main()
