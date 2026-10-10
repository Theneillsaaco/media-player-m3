#!/usr/bin/env python3
"""YouTube Music sidecar: one JSON request per line on stdin, one JSON reply per line on stdout.

  request: {"id": 1, "method": "search", "params": {"query": "...", "limit": 30}}
  reply:   {"id": 1, "ok": true,  "result": ...}
           {"id": 1, "ok": false, "error": "...", "code": "auth_expired"}   (code only for auth problems)

Methods:
  ping, search (songs only), song (metadata of one videoId)
  auth.set {cookie, authuser}        use this cookie header; replies {name}
  auth.import_browser {browser, profile}   read the session from a browser; replies {cookie, name}
  auth.status, auth.clear
  library_playlists                  the user's playlists ("LM" = Liked Music is always first)
  playlist {id, limit}               tracks of a playlist (limit 0 = all)
  rate_song {id, rating}             rating is LIKE or INDIFFERENT

The cookie equals a password: it only ever arrives on stdin and is never printed or logged.
Stream resolution is left to mpv's yt-dlp hook.

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
LIKED = "LM"
_api = None    # anonymous client: search and song metadata
_auth = None   # signed-in client, set by auth.set / auth.import_browser


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
    if _auth is None:
        raise AuthError("auth_required", "No hay sesión de YouTube Music")
    from ytmusicapi.exceptions import YTMusicServerError
    try:
        return call(_auth)
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
    "auth.set": auth_set,
    "auth.import_browser": auth_import_browser,
    "auth.status": auth_status,
    "auth.clear": auth_clear,
    "library_playlists": library_playlists,
    "playlist": playlist,
    "rate_song": rate_song,
}


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
            payload = {"id": rid, "ok": False, "error": (str(exc) or type(exc).__name__)[-500:]}
            if isinstance(exc, AuthError):
                payload["code"] = exc.code
            reply(payload)


if __name__ == "__main__":
    main()
