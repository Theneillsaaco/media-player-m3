"""Extras for the YouTube Music sidecar.

fast resolve, report_playback, loudness, radio, create_playlist, delete_playlist, playlist_add and follow.
server.py hands in itself, so nothing is duplicated:

    import extras
    METHODS.update(extras.build(sys.modules[__name__]))

resolve is replaced by a version that first asks yt-dlp for the audio with the visionOS client and without the
HLS and DASH manifests, which needs no JavaScript solving and is much cheaper; if that fails, the original
resolve (the full set of clients) answers.
"""
import gc
import queue
import re
import sys
import threading
import time
from ytm_util import clock as _clock

VIDEO_ID = re.compile(r"[A-Za-z0-9_-]{11}")
PLAYLIST_ID = re.compile(r"[A-Za-z0-9_-]{2,64}")
CHANNEL_ID = re.compile(r"[A-Za-z0-9_-]{2,100}")
LIKED = "LM"
LIGHT_ARGS = {"youtube": {"player_client": ["visionos"], "skip": ["hls", "dash"]}}

# las que se estaban precargando. Lo usa también la reserva de YoutubeDL de abajo, así que hay como máximo 3 instancias.
RESOLVE_SLOTS = 3
# Cada tantas resoluciones se fuerza una pasada del recolector de basura de Python (yt-dlp deja ciclos de objetos).
COLLECT_EVERY = 25

def _check(value, pattern, what):
    value = str(value or "")
    if not pattern.fullmatch(value):
        raise ValueError("Invalid " + what)
    return value


def build(server):
    slow_resolve = server.METHODS["resolve"]

    # guarda el JavaScript del reproductor de YouTube y sus cachés. Ahora hay una reserva de, como mucho, RESOLVE_SLOTS
    # instancias que se prestan y se devuelven, y el semáforo de server.py se sustituye por uno del mismo tamaño.
    server._resolving = threading.BoundedSemaphore(RESOLVE_SLOTS)
    pool = queue.LifoQueue()
    made = [0]
    count = [0]
    lock = threading.Lock()

    def new_ydl():
        from yt_dlp import YoutubeDL
        return YoutubeDL({
            "format": server.AUDIO_FORMAT,
            "noplaylist": True,
            "quiet": True,
            "no_warnings": True,
            "skip_download": True,
            "socket_timeout": 20,
            "js_runtimes": {"node": {}},
        })

    def checkout():
        try:
            return pool.get_nowait()
        except queue.Empty:
            pass
        with lock:
            create = made[0] < RESOLVE_SLOTS
            if create:
                made[0] += 1
        if not create:
            return pool.get()
        try:
            return new_ydl()
        except Exception:
            with lock:
                made[0] -= 1
            raise

    def collect():
        with lock:
            count[0] += 1
            due = count[0] % COLLECT_EVERY == 0
        if due:
            gc.collect()

    # ---- resolve ----

    def pick(info):
        """The format entry that holds the audio URL, or None."""
        if info.get("url"):
            return info
        parts = info.get("requested_formats") or []
        return next((f for f in parts if f.get("url") and f.get("acodec") not in (None, "none")), None)

    def resolve(params):
        video = str(params.get("id", ""))
        if not server.VIDEO_ID.fullmatch(video):
            raise ValueError("Invalid YouTube video ID")
        try:
            with server._resolving:
                ydl = checkout()
                try:
                    server.apply_cookies(ydl)
                    info = ydl.extract_info("https://music.youtube.com/watch?v=" + video, download=False)
                finally:
                    pool.put(ydl)
            server._remember_stats(video, info)
            collect()
            chosen = pick(info)
            if chosen is None:
                raise ValueError("No playable stream")
            url = chosen["url"]
            headers = chosen.get("http_headers") or info.get("http_headers") or {}
            lines = [f"{k}: {v}" for k, v in headers.items()
                     if k.lower() != "cookie" and "\n" not in f"{k}{v}"]
            return {
                "url": url,
                "headers": lines,
                "expires_at": server._expires_at(url),
                "duration": info.get("duration") or 0,
            }
        except Exception as exc:
            print("resolve: the light client failed (" + (str(exc) or type(exc).__name__)[:120] + "), using the full set",
                  file=sys.stderr)
            return slow_resolve(params)

    # ---- account ----

    def report_playback(params):
        """Tells YouTube Music the account started playing a track: what its recommendations are made from."""
        video = _check(params.get("id"), VIDEO_ID, "YouTube video ID")

        def run(client):
            client.add_history_item(client.get_song(video))

        server.signed_in(run)
        return True

    def loudness(params):
        """How loud YouTube measured the track, in dB: {found, db}. Only a signed-in session gets it."""
        video = _check(params.get("id"), VIDEO_ID, "YouTube video ID")

        def run(client):
            return client._send_request("player", {
                "videoId": video,
                "playbackContext": {"contentPlaybackContext": {"signatureTimestamp": int(time.time() // 86400) - 1}},
            })

        data = server.signed_in(run)
        audio = ((data or {}).get("playerConfig") or {}).get("audioConfig") or {}
        level = audio.get("loudnessDb")
        if level is None:
            level = audio.get("perceptualLoudnessDb")
        if level is None:
            return {"found": False, "db": 0}
        return {"found": True, "db": float(level)}

    # ---- radio ----

    def radio(params):
        """What plays after a track: the songs of its radio, without the track itself."""
        video = _check(params.get("id"), VIDEO_ID, "YouTube video ID")
        limit = min(max(int(params.get("limit", 25)), 1), 50)
        found = server.api().get_watch_playlist(videoId=video, radio=True, limit=limit)
        out = []
        for raw in (found or {}).get("tracks") or []:
            if not isinstance(raw, dict) or not raw.get("videoId") or raw.get("videoId") == video:
                continue
            if raw.get("isAvailable") is False:
                continue
            mapped = server.track(raw)
            if not mapped or mapped["kind"] != "song":
                continue
            if not mapped["seconds"]:  # watch playlists give "length": "3:21"
                mapped["seconds"] = _clock(raw.get("length") or raw.get("duration"))
            out.append(mapped)
        return out

    # ---- library writes ----

    def create_playlist(params):
        title = str(params.get("title", "")).strip()
        if not title:
            raise ValueError("Falta el nombre de la lista")
        video = str(params.get("videoId") or "")
        ids = [_check(video, VIDEO_ID, "YouTube video ID")] if video else None
        made_id = server.signed_in(lambda c: c.create_playlist(title[:150], "", video_ids=ids))
        if not isinstance(made_id, str):  # an error comes back as the whole reply
            raise ValueError("YouTube Music no creó la lista")
        return {"id": made_id}

    def delete_playlist(params):
        pid = _check(params.get("id"), PLAYLIST_ID, "playlist ID")
        if pid == LIKED:
            raise ValueError("Esa lista no se puede eliminar")
        done = server.signed_in(lambda c: c.delete_playlist(pid))
        if isinstance(done, str) and "SUCCEEDED" not in done:
            raise ValueError(done)
        return True

    def playlist_add(params):
        pid = _check(params.get("playlistId"), PLAYLIST_ID, "playlist ID")
        video = _check(params.get("videoId"), VIDEO_ID, "YouTube video ID")
        if pid == LIKED:
            raise ValueError("Usa Me gusta para esa lista")
        done = server.signed_in(lambda c: c.add_playlist_items(pid, [video], duplicates=False))
        status = done.get("status", "") if isinstance(done, dict) else str(done)
        if "SUCCEEDED" not in status:
            raise ValueError("duplicate")  # the usual reason a song is refused: the list already holds it
        return True

    def follow(params):
        channel = _check(params.get("channelId"), CHANNEL_ID, "channel ID")
        on = bool(params.get("on"))
        server.signed_in(lambda c: c.subscribe_artists([channel]) if on else c.unsubscribe_artists([channel]))
        return True

    return {
        "resolve": resolve,
        "report_playback": report_playback,
        "loudness": loudness,
        "radio": radio,
        "create_playlist": create_playlist,
        "delete_playlist": delete_playlist,
        "playlist_add": playlist_add,
        "follow": follow,
    }
