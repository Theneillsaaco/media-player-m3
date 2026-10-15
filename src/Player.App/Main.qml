import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic
import "../QML/Components"
import "../QML/Pages"
import "../QML/Dialogs"

ApplicationWindow {
    id: win
    width: 1000
    height: 680
    minimumWidth: 640
    minimumHeight: 460
    visible: true
    title: "Player"
    color: appTheme.surface

    // Leaf components find the theme through the window: Window.window.theme.
    readonly property var theme: appTheme

    property bool queueOpen: true
    property bool libraryOpen: false
    property bool lyricsOpen: false

    // The player: "mini" (the picture lives in the playback bar and the search or library page shows), "page" (the
    // player takes the left area) or "full" (maximized: the playback bar floats over the picture).
    property string playerMode: "mini"
    readonly property bool playerOpen: playerMode !== "mini"

    PlaybackViewModel { id: playback }
    QueueViewModel { id: queueVm }
    SearchViewModel { id: searchVm }
    LibraryViewModel { id: libraryVm }
    SettingsViewModel { id: settingsVm }
    ThemeViewModel { id: themeVm }
    LyricsViewModel { id: lyricsVm }
    VideoViewModel { id: videoVm }
    Theme { id: appTheme; tokensJson: themeVm.tokensJson }

    SettingsDialog { id: settings; vm: settingsVm; appearanceVm: themeVm; videoVm: videoVm; theme: appTheme }

    function pollAll() {
        settingsVm.poll()
        searchVm.poll()
        libraryVm.poll()
        lyricsVm.poll()
        videoVm.poll()
        playback.poll()
    }

    // Polls only while something is playing, searching or working in the background,
    // so an idle player wakes nothing. It ticks faster while the lyrics are open, to follow the line.
    Timer {
        interval: win.lyricsOpen ? 120 : 250
        repeat: true
        running: (playback.isLoaded && !playback.paused) || searchVm.searching || libraryVm.working || settingsVm.busy || lyricsVm.loading || videoVm.loading
        onTriggered: win.pollAll()
    }

    // Follows the desktop's light/dark setting, only while the mode is "system".
    Timer {
        interval: 1000
        repeat: true
        running: themeVm.mode === "system"
        onTriggered: themeVm.poll()
    }

    // Runs what the desktop asked for (playerctl, media keys). Cheap: it only looks at a queue.
    Timer {
        interval: 250
        repeat: true
        running: true
        onTriggered: playback.pump()
    }

    // Opening the library, or signing in while it is open, loads the playlists.
    onLibraryOpenChanged: if (libraryOpen) libraryVm.loadLibrary()

    // The queue and the lyrics share the right-hand side: opening one closes the other.
    onQueueOpenChanged: if (queueOpen) lyricsOpen = false
    onLyricsOpenChanged: {
        lyricsVm.panelShown(lyricsOpen)
        if (lyricsOpen) queueOpen = false
    }

    // Escape leaves the maximized player.
    Shortcut {
        sequence: "Esc"
        enabled: win.playerMode === "full"
        onActivated: win.playerMode = "page"
    }
    property string accountState: libraryVm.accountState
    onAccountStateChanged: if (libraryOpen) libraryVm.loadLibrary()

    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        RowLayout {
            Layout.fillWidth: true
            Layout.margins: 16
            spacing: 8

            MSearchField {
                id: input
                Layout.fillWidth: true
                placeholderText: "Busca canciones, o pega un enlace o ruta"
                onAccepted: { win.libraryOpen = false; win.playerMode = "mini"; searchVm.search(text) }
            }
            MIconButton {
                glyph: "library"
                selected: win.libraryOpen
                onClicked: {
                    win.libraryOpen = !win.libraryOpen
                    if (win.libraryOpen) win.playerMode = "mini"
                }
            }
            MIconButton {
                glyph: "album"
                selected: win.playerOpen
                onClicked: win.playerMode = win.playerOpen ? "mini" : "page"
            }
            MIconButton {
                glyph: "lyrics"
                selected: win.lyricsOpen
                onClicked: win.lyricsOpen = !win.lyricsOpen
            }
            MIconButton {
                glyph: "queue"
                selected: win.queueOpen
                onClicked: win.queueOpen = !win.queueOpen
            }
            MIconButton {
                glyph: "settings"
                onClicked: settings.open()
            }
        }

        RowLayout {
            Layout.fillWidth: true
            Layout.fillHeight: true
            spacing: 0

            SearchPage {
                Layout.fillWidth: true
                Layout.fillHeight: true
                visible: !win.libraryOpen && !win.playerOpen
                vm: searchVm
            }

            LibraryPage {
                Layout.fillWidth: true
                Layout.fillHeight: true
                visible: win.libraryOpen && !win.playerOpen
                vm: libraryVm
                onSettingsRequested: settings.open()
            }

            PlayerPage {
                id: playerPage
                Layout.fillWidth: true
                Layout.fillHeight: true
                visible: win.playerOpen
                mode: win.playerMode
                playback: playback
                video: videoVm
                theme: appTheme
                videoReady: videoLayer.ready
                videoUnavailable: videoLayer.failed
                onMinimizeRequested: win.playerMode = "mini"
                onMaximizeRequested: win.playerMode = "full"
                onRestoreRequested: win.playerMode = "page"
            }

            QueuePanel {
                Layout.preferredWidth: 340
                Layout.fillHeight: true
                visible: win.queueOpen
                vm: queueVm
            }

            LyricsPanel {
                Layout.preferredWidth: 380
                Layout.fillHeight: true
                visible: win.lyricsOpen
                vm: lyricsVm
            }
        }

        // Where the playback bar sits in the window (it moves onto the video in the maximized player).
        Item {
            id: barHost
            Layout.fillWidth: true
            Layout.preferredHeight: 104
            visible: win.playerMode !== "full"
        }
    }

    PlaybackBar {
        id: bar
        parent: win.playerMode === "full" ? playerPage.dock : barHost
        anchors.fill: parent
        playback: playback
        queue: queueVm
        library: libraryVm
        video: videoVm
        theme: appTheme
        docked: win.playerMode === "full"
        videoInSlot: win.playerMode === "mini" && videoVm.showing
        onVideoButtonClicked: {
            videoVm.toggleVideo()
            if (videoVm.showing && win.playerMode === "mini") win.playerMode = "page"
        }
        onCoverClicked: win.playerMode = win.playerOpen ? "mini" : "page"
    }

    // The one video of the app, moved into whichever slot is on screen so it never restarts.
    VideoLayer {
        id: videoLayer
        parent: win.playerOpen ? playerPage.stage : bar.miniSlot
        anchors.fill: parent
        vm: videoVm
        playback: playback
        fit: win.playerMode === "full"
    }
}
