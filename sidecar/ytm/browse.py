"""Browsing for the YouTube Music sidecar (phase 3F).

home, album, artist, collection (playlist or mix), search_typed, search_suggestions and track_links.
server.py hands in what it already has, so nothing is duplicated:

    import browse
    METHODS.update(browse.build(api=api, signed_in=signed_in, get_auth=lambda: _auth,
                                track=track, artwork=artwork, artist_names=artist_names))

Every list item comes out in one of two shapes:
    {"type": "track", "track": {...}}                                   a song or video (same dict as search)
    {"type": "album" | "artist" | "playlist", "id", "title", "subtitle", "art"}

The item normalisation follows Sung's helper/catalog.py (MIT), rewritten for this protocol.
"""
import re

BROWSE_ID = re.compile(r"[A-Za-z0-9_-]{2,100}")
VIDEO_ID = re.compile(r"[A-Za-z0-9_-]{11}")
FILTERS = {"songs", "videos", "albums", "artists", "playlists"}
SHELF_LIMIT = 14


def _clock(text):
    """'3:21' or '1:02:03' as seconds; 0 when it is not a time."""
    parts = str(text or "").split(":")
    if not 2 <= len(parts) <= 3 or not all(p.isdigit() for p in parts):
        return 0
    total = 0
    for part in parts:
        total = total * 60 + int(part)
    return total


def build(api, signed_in, get_auth, track, artwork, artist_names):
    def check(value, pattern=BROWSE_ID, what="id"):
        value = str(value or "")
        if not pattern.fullmatch(value):
            raise ValueError("Invalid " + what)
        return value

    def subtitle(raw, kind):
        bits = []
        if kind == "album":
            names = artist_names(raw.get("artists"))
            if names:
                bits.append(", ".join(names))
            if raw.get("year"):
                bits.append(str(raw["year"]))
            elif isinstance(raw.get("type"), str) and raw["type"]:
                bits.append(raw["type"])
        elif kind == "artist":
            if raw.get("subscribers"):
                bits.append(str(raw["subscribers"]) + " suscriptores")
        else:
            author = raw.get("author")
            if isinstance(author, str) and author:
                bits.append(author)
            elif isinstance(author, list):
                names = artist_names(author)
                if names:
                    bits.append(", ".join(names))
            elif isinstance(author, dict) and author.get("name"):
                bits.append(str(author["name"]))
            elif raw.get("description"):
                bits.append(str(raw["description"])[:80])
            if raw.get("itemCount"):
                bits.append(str(raw["itemCount"]) + " canciones")
        return " · ".join(bits)

    def item(raw, fallback=""):
        if not isinstance(raw, dict):
            return None

        if raw.get("videoId"):
            if raw.get("isAvailable") is False:
                return None
            mapped = track(raw)
            if not mapped:
                return None
            if not mapped.get("seconds"):  # watch playlists give "length": "3:21"
                mapped["seconds"] = _clock(raw.get("length") or raw.get("duration"))
            return {"type": "track", "track": mapped}

        browse = raw.get("browseId") or ""
        playlist = raw.get("playlistId") or ""
        kind = raw.get("resultType") or fallback
        if not kind:
            kind = "artist" if browse.startswith("UC") else "album" if browse else "playlist" if playlist else ""

        if kind in ("album", "single", "ep"):
            kind, key = "album", browse
        elif kind in ("artist", "profile"):
            kind, key = "artist", browse
        elif kind in ("playlist", "station"):
            kind, key = "playlist", re.sub(r"^VL", "", playlist or browse)
        else:
            return None  # podcasts, episodes...
        if not key:
            return None

        artist_name = raw.get("artist") if isinstance(raw.get("artist"), str) else ""
        return {
            "type": kind,
            "id": key,
            "title": raw.get("title") or raw.get("name") or artist_name or "Sin título",
            "subtitle": subtitle(raw, kind),
            "art": artwork(raw),
        }

    def clean(rows, fallback=""):
        found = (item(raw, fallback) for raw in rows or [])
        return [x for x in found if x]

    def only_entities(rows, fallback):
        return [x for x in clean(rows, fallback) if x["type"] != "track"]

    def tracks_of(rows):
        found = (track(i) for i in rows or [] if isinstance(i, dict) and i.get("videoId") and i.get("isAvailable", True))
        return [t for t in found if t]

    # ---- methods ----

    def search_typed(params):
        query = str(params.get("query", "")).strip()
        kind = str(params.get("filter", "")).strip()
        if not query:
            return []
        if kind not in FILTERS:
            raise ValueError("Invalid filter")
        limit = min(max(int(params.get("limit", 30)), 1), 100)
        return clean(api().search(query, filter=kind, limit=limit))

    def suggestions(params):
        query = str(params.get("query", "")).strip()
        if not query:
            return []
        found = api().get_search_suggestions(query)
        return [s for s in found if isinstance(s, str)][:8]

    def home(params):
        limit = min(max(int(params.get("limit", 4)), 1), 8)
        if params.get("anonymous") or get_auth() is None:
            sections = api().get_home(limit=limit)
        else:
            sections = signed_in(lambda c: c.get_home(limit=limit))
        shelves = []
        for section in sections or []:
            if not isinstance(section, dict):
                continue
            items = clean(section.get("contents"))[:SHELF_LIMIT]
            if items:
                shelves.append({"title": section.get("title") or "", "items": items})
        return shelves

    def album(params):
        key = check(params.get("id"))
        data = api().get_album(key)
        artists = data.get("artists") or []
        tracks = []
        for raw in data.get("tracks") or []:
            if not isinstance(raw, dict) or not raw.get("videoId") or raw.get("isAvailable") is False:
                continue
            # Album tracks carry no cover and often no artist or album: take them from the album.
            filled = dict(raw)
            filled["thumbnails"] = raw.get("thumbnails") or data.get("thumbnails")
            filled["artists"] = raw.get("artists") or artists
            filled["album"] = raw.get("album") or {"name": data.get("title") or ""}
            mapped = track(filled)
            if mapped:
                tracks.append(mapped)
        first = next((a for a in artists if isinstance(a, dict) and a.get("id")), {})
        bits = [", ".join(artist_names(artists)), str(data.get("year") or ""), str(data.get("type") or "")]
        return {
            "id": key,
            "title": data.get("title") or "",
            "subtitle": " · ".join(b for b in bits if b),
            "art": artwork(data),
            "artistId": first.get("id") or "",
            "tracks": tracks,
        }

    def collection(params):
        """A public playlist, or a mix (whose playlist page does not exist: it falls back to the watch playlist)."""
        key = check(re.sub(r"^VL", "", str(params.get("id") or "")))
        data = None
        try:
            data = api().get_playlist(key, limit=200)
        except Exception:
            data = None
        rows = (data or {}).get("tracks") or []
        title = (data or {}).get("title") or ""
        author = ((data or {}).get("author") or {}).get("name") if isinstance((data or {}).get("author"), dict) else ""
        if not rows:
            rows = api().get_watch_playlist(playlistId=key, limit=100).get("tracks") or []
            title = title or "Mix"
            author = author or "Mix"
        mapped = []
        for raw in rows:
            one = item(raw) if isinstance(raw, dict) else None
            if one and one["type"] == "track":
                mapped.append(one["track"])
        art = artwork(data or {}) or (mapped[0]["art"] if mapped else "")
        bits = [author or "", (str(len(mapped)) + " canciones") if mapped else ""]
        return {
            "id": key,
            "title": title,
            "subtitle": " · ".join(b for b in bits if b),
            "art": art,
            "artistId": "",
            "tracks": mapped,
        }

    def artist(params):
        key = check(params.get("id"))
        data = api().get_artist(key)
        songs = data.get("songs") if isinstance(data.get("songs"), dict) else {}

        def part(name, fallback):
            section = data.get(name)
            rows = section.get("results") if isinstance(section, dict) else section
            return only_entities(rows, fallback)

        return {
            "id": key,
            "name": data.get("name") or "",
            "description": (data.get("description") or "").strip()[:500],
            "art": artwork(data),
            "songsId": re.sub(r"^VL", "", songs.get("browseId") or ""),
            "songs": tracks_of(songs.get("results")),
            "albums": part("albums", "album"),
            "singles": part("singles", "album"),
            "related": part("related", "artist"),
        }

    def links(params):
        """The artist and album ids behind a track, so a name in a list can open its page."""
        video = check(params.get("id"), VIDEO_ID, "YouTube video ID")
        tracks = api().get_watch_playlist(video, limit=1).get("tracks") or []
        first = tracks[0] if tracks and isinstance(tracks[0], dict) else {}
        artists = [a for a in first.get("artists") or [] if isinstance(a, dict)]
        main = next((a for a in artists if a.get("id")), {})
        album_info = first.get("album") or {}
        if isinstance(album_info, str):
            album_info = {"name": album_info}
        return {
            "artistId": main.get("id") or "",
            "artistName": main.get("name") or "",
            "albumId": album_info.get("id") or "",
            "albumName": album_info.get("name") or "",
        }

    return {
        "search_typed": search_typed,
        "search_suggestions": suggestions,
        "home": home,
        "album": album,
        "collection": collection,
        "artist": artist,
        "track_links": links,
    }
