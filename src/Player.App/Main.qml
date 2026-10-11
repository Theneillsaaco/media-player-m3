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
    color: "#FEF7FF" // M3 baseline surface

    property bool queueOpen: true
    property bool libraryOpen: false

    PlaybackViewModel { id: playback }
    QueueViewModel { id: queueVm }
    SearchViewModel { id: searchVm }
    LibraryViewModel { id: libraryVm }
    SettingsViewModel { id: settingsVm }

    SettingsDialog { id: settings; vm: settingsVm }

    function pollAll() {
        settingsVm.poll()
        searchVm.poll()
        libraryVm.poll()
        playback.poll()
    }

    // Polls only while something is playing, searching or working in the background,
    // so an idle player wakes nothing.
    Timer {
        interval: 250
        repeat: true
        running: (playback.isLoaded && !playback.paused) || searchVm.searching || libraryVm.working || settingsVm.busy
        onTriggered: win.pollAll()
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
        }

        // Now playing bar
        Rectangle {
            Layout.fillWidth: true
            Layout.preferredHeight: 104
            color: "#F3EDF7"

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
                    Text { text: playback.positionText; color: "#49454F"; font.pixelSize: 12 }
                    MSlider {
                        Layout.fillWidth: true
                        from: 0
                        to: Math.max(1, playback.duration)
                        value: playback.position
                        enabled: playback.isLoaded && playback.duration > 0
                        onChangedByUser: (v) => playback.seek(v)
                    }
                    Text { text: playback.durationText; color: "#49454F"; font.pixelSize: 12 }
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
                            color: "#E6E0E9"
                            Icon { anchors.centerIn: parent; name: "music"; tint: "#49454F"; visible: art.status !== Image.Ready }
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
                                color: "#1D1B20"
                                font.pixelSize: 14
                                font.weight: Font.Medium
                                elide: Text.ElideRight
                            }
                            Text {
                                Layout.fillWidth: true
                                text: playback.currentArtist
                                color: "#49454F"
                                font.pixelSize: 12
                                elide: Text.ElideRight
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
                        Icon { name: "volume"; tint: "#49454F"; size: 20 }
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
