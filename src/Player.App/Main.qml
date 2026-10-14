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
    property bool nowPlayingOpen: false

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

    // The queue, the lyrics and "Reproduciendo" share the right-hand side: opening one closes the others.
    onQueueOpenChanged: if (queueOpen) { lyricsOpen = false; nowPlayingOpen = false }
    onLyricsOpenChanged: {
        lyricsVm.panelShown(lyricsOpen)
        if (lyricsOpen) { queueOpen = false; nowPlayingOpen = false }
    }
    onNowPlayingOpenChanged: {
        videoVm.panelShown(nowPlayingOpen)
        if (nowPlayingOpen) { queueOpen = false; lyricsOpen = false }
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
                onAccepted: { win.libraryOpen = false; searchVm.search(text) }
            }
            MIconButton {
                glyph: "library"
                selected: win.libraryOpen
                onClicked: win.libraryOpen = !win.libraryOpen
            }
            MIconButton {
                glyph: "album"
                selected: win.nowPlayingOpen
                onClicked: win.nowPlayingOpen = !win.nowPlayingOpen
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
                visible: !win.libraryOpen
                vm: searchVm
            }

            LibraryPage {
                Layout.fillWidth: true
                Layout.fillHeight: true
                visible: win.libraryOpen
                vm: libraryVm
                onSettingsRequested: settings.open()
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

            NowPlayingPanel {
                Layout.preferredWidth: 380
                Layout.fillHeight: true
                visible: win.nowPlayingOpen
                vm: videoVm
                playback: playback
            }
        }

        // Now playing bar
        Rectangle {
            Layout.fillWidth: true
            Layout.preferredHeight: 104
            color: appTheme.surfaceContainer

            ColumnLayout {
                anchors.fill: parent
                anchors.leftMargin: 16
                anchors.rightMargin: 16
                anchors.topMargin: 8
                anchors.bottomMargin: 8
                spacing: 0

                RowLayout {
                    Layout.fillWidth: true
                    spacing: 8
                    Text { text: playback.positionText; color: appTheme.onSurfaceVariant; font.pixelSize: 12 }
                    MSlider {
                        Layout.fillWidth: true
                        from: 0
                        to: Math.max(1, playback.duration)
                        value: playback.position
                        enabled: playback.isLoaded && playback.duration > 0
                        onChangedByUser: (v) => playback.seek(v)
                    }
                    Text { text: playback.durationText; color: appTheme.onSurfaceVariant; font.pixelSize: 12 }
                }

                RowLayout {
                    Layout.fillWidth: true
                    Layout.fillHeight: true
                    spacing: 8

                    // Track info
                    RowLayout {
                        Layout.preferredWidth: 280
                        Layout.maximumWidth: 280
                        spacing: 12
                        Rectangle {
                            Layout.preferredWidth: 48
                            Layout.preferredHeight: 48
                            color: appTheme.surfaceContainerHighest
                            Icon { anchors.centerIn: parent; name: "music"; tint: appTheme.onSurfaceVariant; visible: art.status !== Image.Ready }
                            Image {
                                id: art
                                anchors.fill: parent
                                source: playback.currentArt
                                asynchronous: true
                                fillMode: Image.PreserveAspectCrop
                                sourceSize.width: 96
                                sourceSize.height: 96
                            }
                        }
                        ColumnLayout {
                            Layout.fillWidth: true
                            spacing: 2
                            Text {
                                Layout.fillWidth: true
                                text: playback.currentTitle
                                color: appTheme.onSurface
                                font.pixelSize: 14
                                font.weight: Font.Medium
                                elide: Text.ElideRight
                            }
                            Text {
                                Layout.fillWidth: true
                                text: playback.currentArtist
                                color: appTheme.onSurfaceVariant
                                font.pixelSize: 12
                                elide: Text.ElideRight
                            }
                        }
                        MIconButton {
                            visible: videoVm.available
                            glyph: "video"
                            selected: videoVm.showing
                            onClicked: {
                                videoVm.toggleVideo()
                                if (videoVm.showing) win.nowPlayingOpen = true
                            }
                        }
                        MIconButton {
                            visible: libraryVm.canLike
                            glyph: libraryVm.liked ? "favorite" : "favorite_border"
                            selected: libraryVm.liked
                            onClicked: libraryVm.toggleLike()
                        }
                    }

                    Item { Layout.fillWidth: true }

                    // Transport
                    RowLayout {
                        spacing: 4
                        MIconButton {
                            glyph: "shuffle"
                            selected: playback.shuffle
                            onClicked: playback.toggleShuffle()
                        }
                        MIconButton {
                            glyph: "previous"
                            enabled: playback.canPrevious
                            onClicked: playback.previous()
                        }
                        MIconButton {
                            glyph: playback.isLoaded && !playback.paused ? "pause" : "play"
                            filled: true
                            containerSize: 48
                            enabled: queueVm.count > 0
                            onClicked: playback.toggle()
                        }
                        MIconButton {
                            glyph: "next"
                            enabled: playback.canNext
                            onClicked: playback.next()
                        }
                        MIconButton {
                            glyph: playback.repeatMode === "one" ? "repeat_one" : "repeat"
                            selected: playback.repeatMode !== "off"
                            onClicked: playback.cycleRepeat()
                        }
                    }

                    Item { Layout.fillWidth: true }

                    // Volume
                    RowLayout {
                        Layout.preferredWidth: 160
                        spacing: 8
                        Icon { name: "volume"; tint: appTheme.onSurfaceVariant; size: 20 }
                        MSlider {
                            Layout.fillWidth: true
                            from: 0
                            to: 100
                            value: playback.volume
                            live: true
                            onChangedByUser: (v) => playback.setVolume(Math.round(v))
                        }
                    }
                }
            }
        }
    }
}
