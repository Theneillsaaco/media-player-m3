import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic
import "../QML"

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
    property real vol: 100

    PlayerViewModel { id: player }

    // Polls only while something is playing or a search is running, so an idle player wakes nothing.
    Timer {
        interval: 250
        repeat: true
        running: (player.isLoaded && !player.paused) || player.searching
        onTriggered: player.poll()
    }

    function clock(s) {
        const t = Math.max(0, Math.floor(s))
        return Math.floor(t / 60) + ":" + String(t % 60).padStart(2, "0")
    }

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
                onAccepted: player.search(text)
            }
            MIconButton {
                glyph: "queue"
                selected: win.queueOpen
                onClicked: win.queueOpen = !win.queueOpen
            }
        }

        RowLayout {
            Layout.fillWidth: true
            Layout.fillHeight: true
            spacing: 0

            // Search results
            Item {
                Layout.fillWidth: true
                Layout.fillHeight: true

                ListView {
                    id: resultsView
                    anchors.fill: parent
                    clip: true
                    boundsBehavior: Flickable.StopAtBounds
                    model: player.results
                    ScrollBar.vertical: ScrollBar {}
                    delegate: TrackItem {
                        required property int index
                        required property var model
                        width: ListView.view.width
                        heading: model.title
                        supporting: model.artist
                        trailing: model.duration
                        artUrl: model.artUrl
                        showAdd: true
                        onActivated: player.playResult(index)
                        onAddRequested: player.addResult(index)
                    }
                }
                Text {
                    anchors.centerIn: parent
                    width: parent.width - 48
                    horizontalAlignment: Text.AlignHCenter
                    wrapMode: Text.Wrap
                    visible: resultsView.count === 0
                    text: player.status
                    color: "#49454F"
                    font.pixelSize: 14
                }
            }

            // Queue panel
            Rectangle {
                Layout.preferredWidth: 340
                Layout.fillHeight: true
                visible: win.queueOpen
                color: "#F7F2FA"

                ColumnLayout {
                    anchors.fill: parent
                    spacing: 0
                    Text {
                        Layout.margins: 16
                        text: "Cola" + (player.queueCount > 0 ? " · " + player.queueCount : "")
                        color: "#1D1B20"
                        font.pixelSize: 16
                        font.weight: Font.Medium
                    }
                    ListView {
                        id: queueView
                        Layout.fillWidth: true
                        Layout.fillHeight: true
                        clip: true
                        boundsBehavior: Flickable.StopAtBounds
                        model: player.queue
                        ScrollBar.vertical: ScrollBar {}
                        delegate: TrackItem {
                            required property int index
                            required property var model
                            width: ListView.view.width
                            heading: model.title
                            supporting: model.artist
                            trailing: model.duration
                            artUrl: model.artUrl
                            current: index === player.queuePosition
                            onActivated: player.playQueueItem(index)
                        }
                        Text {
                            anchors.centerIn: parent
                            visible: queueView.count === 0
                            text: "La cola está vacía"
                            color: "#49454F"
                            font.pixelSize: 14
                        }
                    }
                }
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
                    Text { text: win.clock(player.position); color: "#49454F"; font.pixelSize: 12 }
                    MSlider {
                        Layout.fillWidth: true
                        from: 0
                        to: Math.max(1, player.duration)
                        value: player.position
                        enabled: player.isLoaded && player.duration > 0
                        onChangedByUser: (v) => player.seek(v)
                    }
                    Text { text: win.clock(player.duration); color: "#49454F"; font.pixelSize: 12 }
                }

                RowLayout {
                    Layout.fillWidth: true
                    Layout.fillHeight: true
                    spacing: 8

                    // Track info
                    RowLayout {
                        Layout.preferredWidth: 240
                        Layout.maximumWidth: 240
                        spacing: 12
                        Rectangle {
                            Layout.preferredWidth: 48
                            Layout.preferredHeight: 48
                            color: "#E6E0E9"
                            Icon { anchors.centerIn: parent; name: "music"; tint: "#49454F"; visible: art.status !== Image.Ready }
                            Image {
                                id: art
                                anchors.fill: parent
                                source: player.currentArt
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
                                text: player.currentTitle
                                color: "#1D1B20"
                                font.pixelSize: 14
                                font.weight: Font.Medium
                                elide: Text.ElideRight
                            }
                            Text {
                                Layout.fillWidth: true
                                text: player.currentArtist
                                color: "#49454F"
                                font.pixelSize: 12
                                elide: Text.ElideRight
                            }
                        }
                    }

                    Item { Layout.fillWidth: true }

                    // Transport
                    RowLayout {
                        spacing: 4
                        MIconButton {
                            glyph: "shuffle"
                            selected: player.shuffle
                            onClicked: player.toggleShuffle()
                        }
                        MIconButton {
                            glyph: "previous"
                            enabled: player.canPrevious
                            onClicked: player.previous()
                        }
                        MIconButton {
                            glyph: player.isLoaded && !player.paused ? "pause" : "play"
                            filled: true
                            containerSize: 48
                            enabled: player.queueCount > 0
                            onClicked: player.toggle()
                        }
                        MIconButton {
                            glyph: "next"
                            enabled: player.canNext
                            onClicked: player.next()
                        }
                        MIconButton {
                            glyph: player.repeatMode === "one" ? "repeat_one" : "repeat"
                            selected: player.repeatMode !== "off"
                            onClicked: player.cycleRepeat()
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
                            value: win.vol
                            live: true
                            onChangedByUser: (v) => { win.vol = v; player.setVolume(Math.round(v)) }
                        }
                    }
                }
            }
        }
    }
}
