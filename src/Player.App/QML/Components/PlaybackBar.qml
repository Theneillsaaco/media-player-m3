import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic

// The playback controls: seek bar, track info, transport and volume. It sits at the bottom of the window, and in
// the maximized player Main.qml moves it onto the video as a floating card (docked), which drops the track info
// because the player already shows it. While the video is minimized, its picture lives in miniSlot, next to the
// title. vm properties: playback = PlaybackViewModel, queue = QueueViewModel, library = LibraryViewModel,
// video = VideoViewModel.
Rectangle {
    id: root

    required property var playback
    required property var queue
    required property var library
    required property var video
    required property var theme

    /// On top of the video in the maximized player: a rounded translucent card without the track info.
    property bool docked: false
    /// The minimized video is on show: its slot is wider than a cover.
    property bool videoInSlot: false

    readonly property Item miniSlot: slot

    signal videoButtonClicked()
    signal coverClicked()

    color: docked ? Qt.alpha(theme.surfaceContainer, 0.94) : theme.surfaceContainer
    radius: docked ? 16 : 0

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
            Text { text: root.playback.positionText; color: root.theme.onSurfaceVariant; font.pixelSize: 12 }
            MSlider {
                Layout.fillWidth: true
                from: 0
                to: Math.max(1, root.playback.duration)
                value: root.playback.position
                enabled: root.playback.isLoaded && root.playback.duration > 0
                onChangedByUser: (v) => root.playback.seek(v)
            }
            Text { text: root.playback.durationText; color: root.theme.onSurfaceVariant; font.pixelSize: 12 }
        }

        RowLayout {
            Layout.fillWidth: true
            Layout.fillHeight: true
            spacing: 8

            // Track info
            RowLayout {
                Layout.preferredWidth: root.docked ? -1 : 300
                Layout.maximumWidth: 300
                spacing: 12

                Rectangle {
                    id: thumb
                    visible: !root.docked
                    Layout.preferredWidth: root.videoInSlot ? 85 : 48
                    Layout.preferredHeight: 48
                    color: root.theme.surfaceContainerHighest
                    clip: true
                    Behavior on Layout.preferredWidth { NumberAnimation { duration: 200 } }

                    Icon { anchors.centerIn: parent; name: "music"; tint: root.theme.onSurfaceVariant; visible: art.status !== Image.Ready }
                    Image {
                        id: art
                        anchors.fill: parent
                        source: root.playback.currentArt
                        asynchronous: true
                        fillMode: Image.PreserveAspectCrop
                        sourceSize.width: 96
                        sourceSize.height: 96
                    }
                    // The minimized video is drawn here (Main.qml parents the video layer to this item).
                    Item { id: slot; anchors.fill: parent }
                    MouseArea { anchors.fill: parent; cursorShape: Qt.PointingHandCursor; onClicked: root.coverClicked() }
                }
                ColumnLayout {
                    visible: !root.docked
                    Layout.fillWidth: true
                    spacing: 2
                    Text {
                        Layout.fillWidth: true
                        text: root.playback.currentTitle
                        color: root.theme.onSurface
                        font.pixelSize: 14
                        font.weight: Font.Medium
                        elide: Text.ElideRight
                    }
                    Text {
                        Layout.fillWidth: true
                        text: root.playback.currentArtist
                        color: root.theme.onSurfaceVariant
                        font.pixelSize: 12
                        elide: Text.ElideRight
                    }
                }
                MIconButton {
                    visible: root.video.available
                    glyph: "video"
                    selected: root.video.showing
                    onClicked: root.videoButtonClicked()
                }
                MIconButton {
                    visible: root.library.canLike
                    glyph: root.library.liked ? "favorite" : "favorite_border"
                    selected: root.library.liked
                    onClicked: root.library.toggleLike()
                }
            }

            Item { Layout.fillWidth: true }

            // Transport
            RowLayout {
                spacing: 4
                MIconButton {
                    glyph: "shuffle"
                    selected: root.playback.shuffle
                    onClicked: root.playback.toggleShuffle()
                }
                MIconButton {
                    glyph: "previous"
                    enabled: root.playback.canPrevious
                    onClicked: root.playback.previous()
                }
                MIconButton {
                    glyph: root.playback.isLoaded && !root.playback.paused ? "pause" : "play"
                    filled: true
                    containerSize: 48
                    enabled: root.queue.count > 0
                    onClicked: root.playback.toggle()
                }
                MIconButton {
                    glyph: "next"
                    enabled: root.playback.canNext
                    onClicked: root.playback.next()
                }
                MIconButton {
                    glyph: root.playback.repeatMode === "one" ? "repeat_one" : "repeat"
                    selected: root.playback.repeatMode !== "off"
                    onClicked: root.playback.cycleRepeat()
                }
            }

            Item { Layout.fillWidth: true }

            // Volume
            RowLayout {
                Layout.preferredWidth: 160
                spacing: 8
                Icon { name: "volume"; tint: root.theme.onSurfaceVariant; size: 20 }
                MSlider {
                    Layout.fillWidth: true
                    from: 0
                    to: 100
                    value: root.playback.volume
                    live: true
                    onChangedByUser: (v) => root.playback.setVolume(Math.round(v))
                }
            }
        }
    }
}
