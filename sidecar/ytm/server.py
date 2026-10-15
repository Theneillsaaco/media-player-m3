#!/usr/bin/env python3
"""YouTube Music sidecar: one JSON request per line on stdin, one JSON reply per line on stdout.

  request: {"id": 1, "method": "search", "params": {"query": "...", "limit": 30}}
  reply:   {"id": 1, "ok": true,  "result": ...}
           {"id": 1, "ok": false, "error": "...", "code": "auth_expired"}   (code only for auth problems)

Requests run concurrently on a small thread pool, so replies can come back in any order
(the id matches them up). At most two resolves run at once, so searches never queue behind them.

Methods:
  ping, search (songs only), song (metadata of one videoId)
  resolve {id}                       direct audio stream: {url, headers, expires_at, duration}
  lyrics {id}                        {found, synced, text, source}; text is LRC ([mm:ss.xx]line) when synced
  video_info {id}                    {id, type, counterpartId, counterpartType}: official video / studio audio / upload
  video_stream {id}                  video-only stream (up to 720p): {url, expires_at, width, height}
  auth.set {cookie, authuser}        use this cookie header; replies {name}
  auth.import_browser {browser, profile}   read the session from a browser; replies {cookie, name}
  auth.status, auth.clear
  library_playlists                  the user's playlists ("LM" = Liked Music is always first)
  playlist {id, limit}               tracks of a playlist (limit 0 = all)
  rate_song {id, rating}             rating is LIKE or INDIFFERENT

The cookie equals a password: it only ever arrives on stdin and is never printed or logged.
resolve is anonymous (no cookie), the same as the yt-dlp hook inside mpv.

Result normalisation (artwork upscaling, artist/album extraction) follows Sung's
helper/catalog.py (MIT), rewritten for this protocol.
"""
import json
import re
import sys
import threading
import time
import browse
from concurrent.futures import ThreadPoolExecutor

# Protocol output goes to the real stdout; anything a library prints goes to stderr.
_out = sys.stdout
sys.stdout = sys.stderr
for _s in (sys.stdin, _out):
    try:
        _s.reconfigure(encoding="utf-8")
    except Exception:
        pass

VIDEO_ID = re.compile(r"[A-Za-z0-9_-]{11}")
LIKED = "LM"
AUDIO_FORMAT = "bestaudio/best"
# Video for the picture only (the sound comes from mpv): H.264 first, since it is the cheapest to decode.
VIDEO_FORMAT = ("bestvideo[height<=720][vcodec^=avc1]/bestvideo[height<=720]/best[height<=720]"
                "/bestvideo/best")
WORKERS = 6
RESOLVE_SLOTS = 2
_api = None    # anonymous client: search and song metadata
_auth = None   # signed-in client, set by auth.set / auth.import_browser
_init = threading.Lock()        # guards the lazy creation of the shared client
_write = threading.Lock()       # one reply line at a time
_resolving = threading.BoundedSemaphore(RESOLVE_SLOTS)
_local = threading.local()      # one YoutubeDL per worker thread, reused between calls


class AuthError(Exception):
    """The Google session is missing, rejected or expired. `code` travels in the reply."""

    def __init__(self, code, message):
        super().__init__(message)
        self.code = code


def _with_timeout(original, timeout):
    def request(*args, **kwargs):
        kwargs.setdefault("timeout", timeout)
        return original(*args, **kwargs)
    return request


def _client(auth=None):
    from ytmusicapi import YTMusic
    client = YTMusic(auth=auth, requests_session=True)
    client._session.request = _with_timeout(client._session.request, 20)
    return client


def api():
    global _api
    if _api is None:
        with _init:
            if _api is None:
                _api = _client()
    return _api


# ---- account ----

def browser_client(cookie, authuser):
    if "__Secure-3PAPISID=" not in cookie:
        raise AuthError("auth_expired", "La cookie no incluye __Secure-3PAPISID: no hay sesión iniciada")
    from ytmusicapi.helpers import initialize_headers
    headers = dict(initialize_headers())
    headers.update({
        "cookie": cookie,
        "authorization": "SAPISIDHASH placeholder",  # ytmusicapi recomputes it on every request
        "x-goog-authuser": str(authuser),
    })
    return _client(json.dumps(headers))


def _rejected(exc):
    return re.match(r"Server returned HTTP (401|403)", str(exc)) is not None


def account_name(client):
    from ytmusicapi.exceptions import YTMusicServerError
    try:
        return client.get_account_info().get("accountName") or ""
    except YTMusicServerError as exc:
        if _rejected(exc):
            raise AuthError("auth_expired", "Google rechazó la sesión") from exc
        raise
    except (KeyError, IndexError, TypeError) as exc:  # a signed-out menu has no account header
        raise AuthError("auth_expired", "Google no reconoce la sesión") from exc


def signed_in(call):
    """Runs call(client) with the signed-in client and turns a 401/403 into auth_expired."""
    client = _auth
    if client is None:
        raise AuthError("auth_required", "No hay sesión de YouTube Music")
    from ytmusicapi.exceptions import YTMusicServerError
    try:
        return call(client)
    except YTMusicServerError as exc:
        if _rejected(exc):
            raise AuthError("auth_expired", "La sesión de YouTube Music caducó") from exc
        raise


def auth_set(params):
    global _auth
    cookie = " ".join(str(params.get("cookie", "")).split())
    client = browser_client(cookie, int(params.get("authuser", 0)))
    name = account_name(client)
    _auth = client
    return {"name": name}


def cookie_header(jar):
    """Cookie header for music.youtube.com from a cookie jar: the live youtube.com cookies."""
    chosen = {}
    for c in jar:
        domain = (c.domain or "").lstrip(".")
        if (domain == "youtube.com" or domain.endswith(".youtube.com")) and c.value is not None and not c.is_expired():
            chosen[c.name] = c.value
    return "; ".join(f"{k}={v}" for k, v in chosen.items())


def auth_import_browser(params):
    global _auth
    from yt_dlp.cookies import SUPPORTED_BROWSERS, extract_cookies_from_browser
    browser = str(params.get("browser", "")).lower()
    if browser not in SUPPORTED_BROWSERS:
        raise ValueError("Navegador no soportado: " + browser)
    profile = str(params.get("profile", "")).strip() or None
    cookie = cookie_header(extract_cookies_from_browser(browser, profile))
    client = browser_client(cookie, 0)
    name = account_name(client)
    _auth = client
    return {"cookie": cookie, "name": name}


def auth_status(_params):
    if _auth is None:
        return {"signedIn": False, "name": ""}
    return {"signedIn": True, "name": signed_in(account_name)}


def auth_clear(_params):
    global _auth
    _auth = None
    return True


# ---- catalogue ----

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


def artist_names(value):
    if isinstance(value, str):
        return [value] if value else []
    return [a["name"] for a in (value or []) if isinstance(a, dict) and a.get("name")]


def names(value):
    return ", ".join(artist_names(value))


def kind_of(item):
    # Studio tracks (ATV) and official music videos (OMV) carry proper artist metadata and can be
    # scrobbled; user uploads and other videos cannot.
    video_type = item.get("videoType") or ""
    return "video" if video_type and not video_type.endswith(("ATV", "OMV")) else "song"


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
        "artists": artist_names(item.get("artists")),
        "album": album.get("name", "") if isinstance(album, dict) else "",
        "art": artwork(item),
        "seconds": item.get("duration_seconds") or 0,
        "kind": kind_of(item),
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
    author = details.get("author", "")
    return {
        "id": video,
        "title": details.get("title") or "Untitled",
        "artist": author,
        "artists": [author] if author else [],
        "album": "",
        "art": artwork({"thumbnails": details.get("thumbnail", {}).get("thumbnails", [])}),
        "seconds": int(details.get("lengthSeconds") or 0),
        "kind": "song",
    }


# ---- streams ----

def _ydl(fmt=AUDIO_FORMAT):
    """One YoutubeDL per worker thread and format, reused between calls."""
    cache = getattr(_local, "ydl", None)
    if cache is None:
        cache = _local.ydl = {}
    ydl = cache.get(fmt)
    if ydl is None:
        from yt_dlp import YoutubeDL
        ydl = cache[fmt] = YoutubeDL({
            "format": fmt,
            "noplaylist": True,
            "quiet": True,
            "no_warnings": True,
            "skip_download": True,
            "socket_timeout": 20,
        })
    return ydl


def _expires_at(url):
    """googlevideo URLs carry their end of life as expire=<unix seconds> (or /expire/<n>/)."""
    found = re.search(r"[?&]expire=(\d+)", url) or re.search(r"/expire/(\d+)", url)
    return int(found.group(1)) if found else int(time.time()) + 3600


def _extract(video, fmt, codec="acodec"):
    """yt-dlp's answer for a video id in a format: (info, the format entry that holds the URL).
    `codec` says which part to take when the selection is a merge: acodec for audio, vcodec for video."""
    with _resolving:
        info = _ydl(fmt).extract_info("https://music.youtube.com/watch?v=" + video, download=False)
    chosen = info
    url = info.get("url")
    if not url:  # a merged selection lists its parts
        parts = info.get("requested_formats") or []
        chosen = next((f for f in parts if f.get(codec) not in (None, "none")), None) or {}
        url = chosen.get("url")
    if not url:
        raise ValueError("No playable stream")
    return info, chosen, url


def resolve(params):
    video = str(params.get("id", ""))
    if not VIDEO_ID.fullmatch(video):
        raise ValueError("Invalid YouTube video ID")
    info, chosen, url = _extract(video, AUDIO_FORMAT)
    headers = chosen.get("http_headers") or info.get("http_headers") or {}
    lines = [f"{k}: {v}" for k, v in headers.items()
             if k.lower() != "cookie" and "\n" not in f"{k}{v}"]  # a cookie must never travel from here
    return {
        "url": url,
        "headers": lines,
        "expires_at": _expires_at(url),
        "duration": info.get("duration") or 0,
    }


# ---- video ----

def video_info(params):
    video = str(params.get("id", ""))
    if not VIDEO_ID.fullmatch(video):
        raise ValueError("Invalid YouTube video ID")
    # The watch playlist is what the song/video switch of YouTube Music is built from: the first track says
    # what this one is and, when there is another version of the song, what that one is (its counterpart).
    tracks = api().get_watch_playlist(video, limit=1).get("tracks") or []
    first = tracks[0] if tracks and isinstance(tracks[0], dict) else {}
    other = first.get("counterpart") or {}
    return {
        "id": video,
        "type": first.get("videoType") or "",
        "counterpartId": other.get("videoId") or "",
        "counterpartType": other.get("videoType") or "",
    }


def video_stream(params):
    video = str(params.get("id", ""))
    if not VIDEO_ID.fullmatch(video):
        raise ValueError("Invalid YouTube video ID")
    info, chosen, url = _extract(video, VIDEO_FORMAT, codec="vcodec")
    return {
        "url": url,
        "expires_at": _expires_at(url),
        "width": int(chosen.get("width") or info.get("width") or 0),
        "height": int(chosen.get("height") or info.get("height") or 0),
    }


# ---- lyrics ----

def _lrc_mark(milliseconds):
    centis = max(0, round(int(milliseconds) / 10))
    minutes, centis = divmod(centis, 6000)
    return f"[{minutes:02d}:{centis // 100:02d}.{centis % 100:02d}]"


def _field(item, name):
    return item.get(name) if isinstance(item, dict) else getattr(item, name, None)


def lyrics(params):
    video = str(params.get("id", ""))
    if not VIDEO_ID.fullmatch(video):
        raise ValueError("Invalid YouTube video ID")
    # The lyrics page is found through the watch playlist; no session is needed for either call.
    browse = api().get_watch_playlist(video, limit=1).get("lyrics")
    if not browse:
        return {"found": False}
    data = api().get_lyrics(browse, timestamps=True)
    if not data:
        return {"found": False}
    source = str(data.get("source") or "")
    body = data.get("lyrics")
    if data.get("hasTimestamps") and isinstance(body, list):
        lines = []
        for line in body:
            text = _field(line, "text")
            start = _field(line, "start_time")
            if text is not None and start is not None:
                lines.append(_lrc_mark(start) + str(text).strip())
        return {"found": bool(lines), "synced": True, "text": "\n".join(lines), "source": source}
    text = body if isinstance(body, str) else ""
    return {"found": bool(text.strip()), "synced": False, "text": text, "source": source}


# ---- library (needs a session) ----

def _count(value):
    digits = re.sub(r"\D", "", str(value or ""))
    return int(digits) if digits else 0


def library_playlists(_params):
    found = signed_in(lambda c: c.get_library_playlists(limit=None))
    lists = [
        {"id": p["playlistId"], "title": p.get("title") or "Sin título",
         "count": _count(p.get("count")), "art": artwork(p)}
        for p in found if isinstance(p, dict) and p.get("playlistId")
    ]
    liked = next((p for p in lists if p["id"] == LIKED), None)
    if liked is None:
        liked = {"id": LIKED, "title": "", "count": 0, "art": ""}
    else:
        lists.remove(liked)
    liked["title"] = "Me gusta"
    return [liked] + lists


def playlist(params):
    pid = str(params.get("id", ""))
    if not re.fullmatch(r"[A-Za-z0-9_-]{2,64}", pid):
        raise ValueError("Invalid playlist ID")
    limit = int(params.get("limit", 0)) or None  # 0 = every track, paginated by ytmusicapi
    data = signed_in(lambda c: c.get_playlist(pid, limit=limit))
    tracks = [t for t in (track(i) for i in data.get("tracks") or []
                          if isinstance(i, dict) and i.get("isAvailable", True)) if t]
    return {"title": data.get("title") or "", "tracks": tracks}


def rate_song(params):
    video = str(params.get("id", ""))
    if not VIDEO_ID.fullmatch(video):
        raise ValueError("Invalid YouTube video ID")
    from ytmusicapi.models.content.enums import LikeStatus
    ratings = {"LIKE": LikeStatus.LIKE, "INDIFFERENT": LikeStatus.INDIFFERENT}
    rating = ratings.get(str(params.get("rating", "")).upper())
    if rating is None:
        raise ValueError("rating must be LIKE or INDIFFERENT")
    signed_in(lambda c: c.rate_song(video, rating))
    return True


METHODS = {
    "ping": lambda p: "pong",
    "search": search,
    "song": song,
    "resolve": resolve,
    "lyrics": lyrics,
    "video_info": video_info,
    "video_stream": video_stream,
    "auth.set": auth_set,
    "auth.import_browser": auth_import_browser,
    "auth.status": auth_status,
    "auth.clear": auth_clear,
    "library_playlists": library_playlists,
    "playlist": playlist,
    "rate_song": rate_song,
}

METHODS.update(browse.build(api=api, signed_in=signed_in, get_auth=lambda: _auth,
                            track=track, artwork=artwork, artist_names=artist_names))

def reply(obj):
    line = json.dumps(obj, ensure_ascii=False) + "\n"
    with _write:
        _out.write(line)
        _out.flush()


def handle(line):
    rid = None
    try:
        req = json.loads(line)
        rid = req.get("id")
        result = METHODS[req["method"]](req.get("params") or {})
        reply({"id": rid, "ok": True, "result": result})
    except Exception as exc:  # one bad request must never kill the process
        payload = {"id": rid, "ok": False, "error": (str(exc) or type(exc).__name__)[-500:]}
        if isinstance(exc, AuthError):
            payload["code"] = exc.code
        reply(payload)


def main():
    with ThreadPoolExecutor(max_workers=WORKERS, thread_name_prefix="ytm") as pool:
        for line in sys.stdin:
            line = line.strip()
            if line:
                pool.submit(handle, line)


if __name__ == "__main__":
    main()
