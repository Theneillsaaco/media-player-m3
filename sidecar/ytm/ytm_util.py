"""Small helpers shared by the modules of the YouTube Music sidecar."""


def clock(text):
    """'3:21' or '1:02:03' as seconds; 0 when it is not a time."""
    parts = str(text or "").split(":")
    if not 2 <= len(parts) <= 3 or not all(p.isdigit() for p in parts):
        return 0
    total = 0
    for part in parts:
        total = total * 60 + int(part)
    return total
