import QtQuick

// The color roles of the current Material 3 scheme, one property per role. tokensJson is what the
// ThemeViewModel provides: a JSON object of "#rrggbb" strings. Until it arrives, and for any role it lacks,
// the light Tonal spot defaults below are used. Everything else in the UI reads these through the window:
// Window.window.theme (leaf components) or a theme property that Main.qml passes down (pages and dialogs).
QtObject {
    property string tokensJson: ""
    readonly property var tokens: parse(tokensJson)

    function parse(json) {
        try { return json.length > 0 ? JSON.parse(json) : ({}) } catch (e) { return ({}) }
    }
    function role(name, fallback) {
        const value = tokens[name]
        return value ? value : fallback
    }

    readonly property color background: role("background", "#fdf7ff")
    readonly property color surface: role("surface", "#fdf7ff")
    readonly property color surfaceDim: role("surfaceDim", "#ded8e0")
    readonly property color surfaceBright: role("surfaceBright", "#fdf7ff")
    readonly property color surfaceContainerLowest: role("surfaceContainerLowest", "#ffffff")
    readonly property color surfaceContainerLow: role("surfaceContainerLow", "#f8f2fa")
    readonly property color surfaceContainer: role("surfaceContainer", "#f2ecf4")
    readonly property color surfaceContainerHigh: role("surfaceContainerHigh", "#ece6ee")
    readonly property color surfaceContainerHighest: role("surfaceContainerHighest", "#e6e0e9")
    readonly property color surfaceVariant: role("surfaceVariant", "#e7e0eb")
    readonly property color surfaceTint: role("surfaceTint", "#65558f")
    readonly property color onBackground: role("onBackground", "#1d1b20")
    readonly property color onSurface: role("onSurface", "#1d1b20")
    readonly property color onSurfaceVariant: role("onSurfaceVariant", "#49454e")
    readonly property color outline: role("outline", "#7a757f")
    readonly property color outlineVariant: role("outlineVariant", "#cac4cf")
    readonly property color inverseSurface: role("inverseSurface", "#322f35")
    readonly property color inverseOnSurface: role("inverseOnSurface", "#f5eff7")
    readonly property color inversePrimary: role("inversePrimary", "#cfbdfe")
    readonly property color scrim: role("scrim", "#000000")
    readonly property color shadow: role("shadow", "#000000")
    readonly property color primary: role("primary", "#65558f")
    readonly property color onPrimary: role("onPrimary", "#ffffff")
    readonly property color primaryContainer: role("primaryContainer", "#e9ddff")
    readonly property color onPrimaryContainer: role("onPrimaryContainer", "#4d3d75")
    readonly property color secondary: role("secondary", "#625b71")
    readonly property color onSecondary: role("onSecondary", "#ffffff")
    readonly property color secondaryContainer: role("secondaryContainer", "#e8def8")
    readonly property color onSecondaryContainer: role("onSecondaryContainer", "#4a4458")
    readonly property color tertiary: role("tertiary", "#7e5260")
    readonly property color onTertiary: role("onTertiary", "#ffffff")
    readonly property color tertiaryContainer: role("tertiaryContainer", "#ffd9e3")
    readonly property color onTertiaryContainer: role("onTertiaryContainer", "#633b48")
    readonly property color error: role("error", "#ba1a1a")
    readonly property color onError: role("onError", "#ffffff")
    readonly property color errorContainer: role("errorContainer", "#ffdad6")
    readonly property color onErrorContainer: role("onErrorContainer", "#93010a")
    readonly property color onPrimaryFixed: role("onPrimaryFixed", "#201047")
    readonly property color onPrimaryFixedVariant: role("onPrimaryFixedVariant", "#4d3d75")
    readonly property color onSecondaryFixed: role("onSecondaryFixed", "#1e192b")
    readonly property color onSecondaryFixedVariant: role("onSecondaryFixedVariant", "#4a4458")
    readonly property color onTertiaryFixed: role("onTertiaryFixed", "#31101d")
    readonly property color onTertiaryFixedVariant: role("onTertiaryFixedVariant", "#633b48")
    readonly property color primaryFixed: role("primaryFixed", "#e9ddff")
    readonly property color primaryFixedDim: role("primaryFixedDim", "#cfbdfe")
    readonly property color secondaryFixed: role("secondaryFixed", "#e8def8")
    readonly property color secondaryFixedDim: role("secondaryFixedDim", "#cbc2db")
    readonly property color tertiaryFixed: role("tertiaryFixed", "#ffd9e3")
    readonly property color tertiaryFixedDim: role("tertiaryFixedDim", "#efb8c8")
}
