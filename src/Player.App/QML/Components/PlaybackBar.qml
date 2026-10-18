import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic

// The playback controls: seek bar, then three blocks: track info on the left, the transport in the middle and the
// volume on the right. The left and right blocks always share the free width equally, so the transport stays
// exactly centred. The bar floats as a rounded card at the bottom of the window; in the maximized player Main.qml
// moves it onto the video (docked), which drops the cover and the title because the player already shows them.
// While the video is minimized, its picture lives in miniSlot, in place of the cover. The title opens the album
// and the artist opens the artist page. The video on/off button is not here: it is with the window buttons of
// the player. vm properties: playback = PlaybackViewModel, queue = QueueViewModel, library = LibraryViewModel,
// video = VideoViewModel.
Rectangle {
    id: root

    required property var playback
    required property var queue
    required property var library
    required property var video
    required property var theme
    readonly property var i18n: Window.window ? Window.window.i18n : null

    /// On top of the video in the maximized player: a translucent card without the cover and the title.
    property bool docked: false
    /// The minimized video is on show: its slot is wider than a cover.
    property bool videoInSlot: false
    /// Volume to come back to after a mute.
    property int lastVolume: 60

    readonly property Item miniSlot: slot
    readonly property bool linkable: root.playback.currentId !== "" && root.playback.isLoaded

    signal coverClicked()

    function tr(key) { return i18n ? i18n.t(key) : key }

    color: docked ? Qt.alpha(theme.surfaceContainerHighest, 0.94) : theme.surfaceContainerHigh
    radius: 28

    ColumnLayout {
        anchors.fill: parent
        anchors.leftMargin: 20
        anchors.rightMargin: 20
        anchors.topMargin: 8
        anchors.bottomMargin: 8
        spacing: 0

        RowLayout {
            Layout.fillWidth: true
            spacing: 8
            Text { text: root.playback.positionText; color: root.theme.inkSurfaceVariant; font.pixelSize: 12 }
            MSlider {
                Layout.fillWidth: true
                from: 0
                to: Math.max(1, root.playback.duration)
                value: root.playback.position
                enabled: root.playback.isLoaded && root.playback.duration > 0
                onChangedByUser: (v) => root.playback.seek(v)
            }
            Text { text: root.playback.durationText; color: root.theme.inkSurfaceVariant; font.pixelSize: 12 }
        }

        RowLayout {
            Layout.fillWidth: true
            Layout.fillHeight: true
            spacing: 8

            // Left block: cover, title and artist, like.
            Item {
                Layout.fillWidth: true
                Layout.preferredWidth: 1
                Layout.fillHeight: true

                RowLayout {
                    anchors.left: parent.left
                    anchors.verticalCenter: parent.verticalCenter
                    width: Math.min(parent.width, 400)
                    spacing: 12

                    Item {
                        id: thumb
                        visible: !root.docked
                        Layout.preferredWidth: root.videoInSlot ? 85 : 48
                        Layout.preferredHeight: 48
                        Behavior on Layout.preferredWidth { NumberAnimation { duration: 200 } }

                        Cover {
                            anchors.fill: parent
                            source: root.playback.currentArt
                            radius: 8
                        }
                        // The minimized video is drawn here (Main.qml parents the video layer to this item).
                        Item { id: slot; anchors.fill: parent }
                        MouseArea { anchors.fill: parent; cursorShape: Qt.PointingHandCursor; onClicked: root.coverClicked() }
                    }

                    // The texts take the width the layout gives them and elide; the click areas are only as wide as
                    // the text itself, so the empty space next to a short title does nothing.
                    ColumnLayout {
                        visible: !root.docked
                        Layout.fillWidth: true
                        Layout.minimumWidth: 0
                        spacing: 2

                        Text {
                            id: titleText
                            Layout.fillWidth: true
                            text: root.playback.currentTitle
                            color: titleLink.containsMouse && root.linkable ? root.theme.primary : root.theme.inkSurface
                            font.pixelSize: 14
                            font.weight: Font.Medium
                            font.underline: titleLink.containsMouse && root.linkable
                            elide: Text.ElideRight

                            MouseArea {
                                id: titleLink
                                width: Math.min(titleText.implicitWidth, titleText.width)
                                height: titleText.height
                                enabled: root.linkable
                                hoverEnabled: true
                                cursorShape: root.linkable ? Qt.PointingHandCursor : Qt.ArrowCursor
                                onClicked: Window.window.openAlbumOf(root.playback.currentId)
                            }
                        }
                        Text {
                            id: artistText
                            Layout.fillWidth: true
                            text: root.playback.currentArtist
                            color: artistLink.containsMouse && root.linkable ? root.theme.primary : root.theme.inkSurfaceVariant
                            font.pixelSize: 12
                            font.underline: artistLink.containsMouse && root.linkable
                            elide: Text.ElideRight

                            MouseArea {
                                id: artistLink
                                width: Math.min(artistText.implicitWidth, artistText.width)
                                height: artistText.height
                                enabled: root.linkable
                                hoverEnabled: true
                                cursorShape: root.linkable ? Qt.PointingHandCursor : Qt.ArrowCursor
                                onClicked: Window.window.openArtistOf(root.playback.currentId)
                            }
                        }
                    }

                    MIconButton {
                        visible: root.library.canLike
                        glyph: root.library.liked ? "favorite" : "favorite_border"
                        tip: root.tr("tip.like")
                        selected: root.library.liked
                        onClicked: root.library.toggleLike()
                    }
                }
            }

            // Middle block: the transport.
            RowLayout {
                spacing: 4
                MIconButton {
                    glyph: "shuffle"
                    tip: root.tr("tip.shuffle")
                    selected: root.playback.shuffle
                    onClicked: root.playback.toggleShuffle()
                }
                MIconButton {
                    glyph: "previous"
                    tip: root.tr("tip.previous")
                    enabled: root.playback.canPrevious
                    onClicked: root.playback.previous()
                }
                MIconButton {
                    glyph: root.playback.isLoaded && !root.playback.paused ? "pause" : "play"
                    tip: root.playback.isLoaded && !root.playback.paused ? root.tr("tip.pause") : root.tr("tip.play")
                    filled: true
                    containerSize: 48
                    enabled: root.queue.count > 0
                    onClicked: root.playback.toggle()
                }
                MIconButton {
                    glyph: "next"
                    tip: root.tr("tip.next")
                    enabled: root.playback.canNext
                    onClicked: root.playback.next()
                }
                MIconButton {
                    glyph: root.playback.repeatMode === "one" ? "repeat_one" : "repeat"
                    tip: root.tr("tip.repeat")
                    selected: root.playback.repeatMode !== "off"
                    onClicked: root.playback.cycleRepeat()
                }
            }

            // Right block: volume, as wide as the left block (up to 190 px), against the right edge.
            Item {
                Layout.fillWidth: true
                Layout.preferredWidth: 1
                Layout.fillHeight: true

                RowLayout {
                    anchors.right: parent.right
                    anchors.verticalCenter: parent.verticalCenter
                    width: Math.min(parent.width, 190)
                    spacing: 4

                    MIconButton {
                        glyph: root.playback.volume > 0 ? "volume" : "volume_off"
                        tip: root.playback.volume > 0 ? root.tr("tip.mute") : root.tr("tip.unmute")
                        onClicked: {
                            if (root.playback.volume > 0) {
                                root.lastVolume = root.playback.volume
                                root.playback.setVolume(0)
                            } else {
                                root.playback.setVolume(root.lastVolume > 0 ? root.lastVolume : 60)
                            }
                        }
                    }
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
}
