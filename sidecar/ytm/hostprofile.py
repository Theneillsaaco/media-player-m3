"""Where a browser keeps its profile when the sidecar runs inside a Flatpak.

yt-dlp looks for browsers under $XDG_CONFIG_HOME, which in a Flatpak is the app's own folder, so it never
sees ~/.config/mozilla or ~/.config/BraveSoftware. HOME is still the real home, so the profile can be found
there (the manifest only gives read access) and handed to yt-dlp as a path. Outside a Flatpak this returns None
and yt-dlp does what it always did.
"""
import os

FLATPAK = os.path.exists("/.flatpak-info")

# Folder under ~/.config of each Chromium-based browser (the names yt-dlp supports).
_CHROMIUM = {
    "chrome": "google-chrome",
    "chromium": "chromium",
    "brave": "BraveSoftware/Brave-Browser",
    "edge": "microsoft-edge",
    "vivaldi": "vivaldi",
    "opera": "opera",
}

# Firefox: classic folder, the XDG one newer versions use, and the Flatpak build of Firefox.
_FIREFOX = (
    ".mozilla/firefox",
    ".config/mozilla/firefox",
    ".var/app/org.mozilla.firefox/.mozilla/firefox",
)


def default_profile(browser):
    """Path of the browser's profile folder in the real home, or None to let yt-dlp look by itself."""
    if not FLATPAK:
        return None
    if browser == "firefox":
        candidates = _FIREFOX
    elif browser in _CHROMIUM:
        candidates = (".config/" + _CHROMIUM[browser],)
    else:
        return None
    home = os.path.expanduser("~")
    for relative in candidates:
        path = os.path.join(home, relative)
        if os.path.isdir(path):
            return path
    return None