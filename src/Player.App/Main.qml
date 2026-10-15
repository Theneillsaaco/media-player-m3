import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic
import "../QML/Components"
import "../QML/Pages"
import "../QML/Dialogs"

ApplicationWindow {
    id: win
    width: 1100
    height: 700
    minimumWidth: 720
    minimumHeight: 480
    visible: true
    title: "Player"
    color: appTheme.surface

    // Leaf components find the theme through the window: Window.window.theme.
    readonly property var theme: appTheme

    property bool queueOpen: true
    property bool lyricsOpen: false

    // The player: "mini" (the picture lives in the playback bar and the pages show), "page" (the player takes the
    // left area) or "full" (maximized: the playback bar floats over the picture).
    property string playerMode: "mini"
    readonly property bool playerOpen: playerMode !== "mini"

    // Width of the queue and lyrics side panels: they give way on narrow windows.
    readonly property int sideWidth: Math.round(Math.min(380, Math.max(280, width * 0.28)))

    PlaybackViewModel { id: playback }
    QueueViewModel { id: queueVm }
    SearchViewModel { id: searchVm }
    LibraryViewModel { id: libraryVm }
    SettingsViewModel { id: settingsVm }
    ThemeViewModel { id: themeVm }
    LyricsViewModel { id: lyricsVm }
    VideoViewModel { id: videoVm }
    NavigationViewModel { id: navVm }
    Theme { id: appTheme; tokensJson: themeVm.tokensJson }

    SettingsDialog { id: settings; vm: settingsVm; appearanceVm: themeVm; videoVm: videoVm; theme: appTheme }

    // Called by the lists and the playback bar: a name opens its page, from wherever the user is.
    function openEntity(kind, entityId) {
        playerMode = "mini"
        navVm.openEntity(kind, entityId)
    }
    function openArtistOf(videoId) {
        playerMode = "mini"
        navVm.openTrackArtist(videoId)
    }
    function openAlbumOf(videoId) {
        playerMode = "mini"
        navVm.openTrackAlbum(videoId)
    }

    function pollAll() {
        settingsVm.poll()
        searchVm.poll()
        libraryVm.poll()
        lyricsVm.poll()
        videoVm.poll()
        navVm.poll()
        playback.poll()
    }

    // Polls only while something is playing, searching or working in the background,
    // so an idle player wakes nothing. It ticks faster while the lyrics are open, to follow the line.
    Timer {
        interval: win.lyricsOpen ? 120 : 250
        repeat: true
        running: (playback.isLoaded && !playback.paused) || searchVm.searching || libraryVm.working || settingsVm.busy || lyricsVm.loading || videoVm.loading || navVm.working
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

    // The snackbar goes away by itself.
    Timer {
        interval: 4000
        running: navVm.message.length > 0
        onTriggered: navVm.dismissMessage()
    }

    // Opening the library, or signing in while it is open, loads the playlists.
    property string currentPage: navVm.page
    onCurrentPageChanged: if (currentPage === "library") libraryVm.loadLibrary()
    property string accountState: libraryVm.accountState
    onAccountStateChanged: if (currentPage === "library") libraryVm.loadLibrary()

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
    Shortcut {
        sequence: "Alt+Left"
        onActivated: navVm.goBack()
    }

    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        // Top bar: back, search and the panels
        RowLayout {
            Layout.fillWidth: true
            Layout.leftMargin: 12
            Layout.rightMargin: 12
            Layout.topMargin: 8
            Layout.bottomMargin: 4
            spacing: 8

            MIconButton {
                glyph: "back"
                enabled: win.playerOpen || navVm.canBack
                onClicked: {
                    if (win.playerOpen) win.playerMode = "mini"
                    else navVm.goBack()
                }
            }
            MSearchField {
                id: input
                Layout.fillWidth: true
                placeholderText: "Busca canciones, o pega un enlace o ruta"
                onAccepted: {
                    win.playerMode = "mini"
                    navVm.goRoot("search")
                    searchVm.search(text)
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
            Layout.leftMargin: 4
            Layout.rightMargin: 12
            spacing: 8

            NavRail {
                Layout.fillHeight: true
                Layout.preferredWidth: 80
                current: navVm.section
                onChosen: (key) => {
                    win.playerMode = "mini"
                    navVm.goRoot(key)
                }
            }

            // The pages and the player share one rounded card.
            Rectangle {
                id: stageCard
                Layout.fillWidth: true
                Layout.fillHeight: true
                radius: win.playerMode === "full" ? 0 : 20
                clip: true
                color: win.playerMode === "full" ? "#000000" : appTheme.surfaceContainerLow

                SearchPage {
                    anchors.fill: parent
                    visible: !win.playerOpen && navVm.page === "search"
                    vm: searchVm
                    onEntityActivated: (kind, entityId) => win.openEntity(kind, entityId)
                }

                HomePage {
                    anchors.fill: parent
                    visible: !win.playerOpen && navVm.page === "home"
                    nav: navVm
                    onEntityActivated: (kind, entityId) => win.openEntity(kind, entityId)
                }

                LibraryPage {
                    anchors.fill: parent
                    visible: !win.playerOpen && navVm.page === "library"
                    vm: libraryVm
                    onSettingsRequested: settings.open()
                }

                CollectionPage {
                    anchors.fill: parent
                    visible: !win.playerOpen && (navVm.page === "album" || navVm.page === "collection")
                    nav: navVm
                }

                ArtistPage {
                    anchors.fill: parent
                    visible: !win.playerOpen && navVm.page === "artist"
                    nav: navVm
                    onEntityActivated: (kind, entityId) => win.openEntity(kind, entityId)
                }

                PlayerPage {
                    id: playerPage
                    anchors.fill: parent
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
            }

            QueuePanel {
                Layout.preferredWidth: win.sideWidth
                Layout.fillHeight: true
                visible: win.queueOpen
                radius: 20
                clip: true
                vm: queueVm
            }

            LyricsPanel {
                Layout.preferredWidth: win.sideWidth + 20
                Layout.fillHeight: true
                visible: win.lyricsOpen
                radius: 20
                clip: true
                vm: lyricsVm
            }
        }

        // Where the playback bar sits in the window (it moves onto the video in the maximized player).
        Item {
            id: barHost
            Layout.fillWidth: true
            Layout.leftMargin: 12
            Layout.rightMargin: 12
            Layout.topMargin: 8
            Layout.bottomMargin: 12
            Layout.preferredHeight: 96
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

    // Snackbar: short messages ("this track has no album"...).
    Rectangle {
        id: snack
        anchors.horizontalCenter: parent.horizontalCenter
        anchors.bottom: parent.bottom
        anchors.bottomMargin: 124
        z: 100
        width: Math.min(snackText.implicitWidth + 40, parent.width - 48)
        height: 48
        radius: 12
        color: appTheme.inverseSurface
        opacity: navVm.message.length > 0 ? 1 : 0
        visible: opacity > 0
        Behavior on opacity { NumberAnimation { duration: 150 } }

        Text {
            id: snackText
            anchors.fill: parent
            anchors.leftMargin: 20
            anchors.rightMargin: 20
            verticalAlignment: Text.AlignVCenter
            text: navVm.message
            color: appTheme.inverseOnSurface
            font.pixelSize: 14
            elide: Text.ElideRight
        }
    }
}
