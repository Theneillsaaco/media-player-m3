import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic

// The player, in the left area where the pages are: the cover of the playing track, or its music video,
// as YouTube Music's player page does it. mode is "page" (the picture centred with the title under it, the playback
// bar stays at the bottom of the window) or "full" (the picture takes the whole area and the playback bar floats on
// it as a card; the buttons and the card fade out after a few idle seconds while it plays). The right side stays
// free for the queue and the lyrics. stage is where Main.qml puts the video; dock is where it puts the playback bar.
// The window buttons (video on/off, maximize or restore, minimize) are in the top right corner of both modes.
Rectangle {
    id: root

    required property var playback
    required property var video
    required property var theme
    readonly property var i18n: Window.window ? Window.window.i18n : null

    property string mode: "page"
    /// The video layer could not load QtMultimedia.
    property bool videoUnavailable: false
    /// The video on stage has frames to show, so the cover can step aside.
    property bool videoReady: false

    readonly property bool full: mode === "full"
    readonly property Item stage: stageRect
    readonly property Item dock: dockHost

    signal minimizeRequested()
    signal maximizeRequested()
    signal restoreRequested()

    function tr(key) { return i18n ? i18n.t(key) : key }

    // Maximized: the buttons and the card go away when the mouse has been still for a while, unless it is paused.
    property bool chrome: true
    readonly property bool chromeShown: !full || chrome || playback.paused

    function touch() {
        chrome = true
        idle.restart()
    }

    onFullChanged: touch()

    Timer {
        id: idle
        interval: 3000
        onTriggered: root.chrome = false
    }

    HoverHandler {
        cursorShape: root.chromeShown ? Qt.ArrowCursor : Qt.BlankCursor
        onPointChanged: root.touch()
    }

    // Transparent in "page" mode: the rounded card of Main.qml is the background.
    color: full ? "#000000" : "transparent"

    // ---- the picture ----

    Item {
        id: area
        anchors.fill: parent
        anchors.margins: root.full ? 0 : 24

        readonly property real textHeight: root.full ? 0 : 76
        readonly property real roomHeight: Math.max(0, height - textHeight)

        Rectangle {
            id: stageRect
            anchors.horizontalCenter: parent.horizontalCenter
            y: root.full ? 0 : Math.max(0, (area.roomHeight - height) / 2)
            width: root.full ? area.width : root.video.showing ? Math.min(area.width, area.roomHeight * 16 / 9) : Math.min(area.width, area.roomHeight)
            height: root.full ? area.height : root.video.showing ? width * 9 / 16 : width
            radius: root.full ? 0 : 16
            clip: true
            color: root.full ? "#000000" : root.theme.surfaceContainerHighest

            Icon {
                anchors.centerIn: parent
                name: "music"
                size: 64
                visible: cover.status !== Image.Ready
            }
            Image {
                id: cover
                anchors.fill: parent
                source: root.playback.currentArt
                asynchronous: true
                fillMode: root.full ? Image.PreserveAspectFit : Image.PreserveAspectCrop
                sourceSize.width: 544
                sourceSize.height: 544
                opacity: root.videoReady ? 0 : 1
                Behavior on opacity { NumberAnimation { duration: 300 } }
            }

            // The video layer is parented here by Main.qml, on top of the cover.

            MouseArea {
                anchors.fill: parent
                z: 10
                onClicked: root.playback.toggle()
            }
        }

        // Title and artist under the picture (the maximized player shows them over it instead)
        ColumnLayout {
            visible: !root.full
            anchors.top: stageRect.bottom
            anchors.topMargin: 16
            anchors.horizontalCenter: stageRect.horizontalCenter
            width: Math.min(stageRect.width, 640)
            spacing: 4

            Text {
                Layout.fillWidth: true
                horizontalAlignment: Text.AlignHCenter
                text: root.playback.currentTitle
                color: root.theme.inkSurface
                font.pixelSize: 20
                font.weight: Font.Medium
                elide: Text.ElideRight
            }
            Text {
                Layout.fillWidth: true
                horizontalAlignment: Text.AlignHCenter
                text: root.playback.currentArtist
                color: root.theme.inkSurfaceVariant
                font.pixelSize: 14
                elide: Text.ElideRight
            }
            Text {
                Layout.fillWidth: true
                horizontalAlignment: Text.AlignHCenter
                visible: root.videoUnavailable
                text: root.tr("video.needsQt")
                color: root.theme.error
                font.pixelSize: 12
                wrapMode: Text.Wrap
            }
            Text {
                Layout.fillWidth: true
                horizontalAlignment: Text.AlignHCenter
                visible: root.video.message.length > 0
                text: root.video.message
                color: root.theme.inkSurfaceVariant
                font.pixelSize: 12
            }
        }
    }

    // ---- over the picture (maximized) ----

    Rectangle {
        id: topScrim
        visible: root.full
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.top: parent.top
        height: 96
        opacity: root.chromeShown ? 1 : 0
        Behavior on opacity { NumberAnimation { duration: 250 } }
        gradient: Gradient {
            GradientStop { position: 0.0; color: Qt.rgba(0, 0, 0, 0.7) }
            GradientStop { position: 1.0; color: Qt.rgba(0, 0, 0, 0) }
        }

        ColumnLayout {
            anchors.left: parent.left
            anchors.right: parent.right
            anchors.top: parent.top
            anchors.leftMargin: 24
            anchors.rightMargin: 220
            anchors.topMargin: 16
            spacing: 2
            Text {
                Layout.fillWidth: true
                text: root.playback.currentTitle
                color: "#FFFFFF"
                font.pixelSize: 20
                font.weight: Font.Medium
                elide: Text.ElideRight
            }
            Text {
                Layout.fillWidth: true
                text: root.playback.currentArtist
                color: "#E6E0E9"
                font.pixelSize: 14
                elide: Text.ElideRight
            }
        }
    }

    // The window buttons: video on or off, maximize or restore, minimize to the playback bar.
    Rectangle {
        anchors.top: parent.top
        anchors.right: parent.right
        anchors.margins: 12
        width: buttons.implicitWidth + 8
        height: 48
        radius: 24
        color: Qt.alpha(root.theme.surfaceContainer, 0.88)
        opacity: root.chromeShown ? 1 : 0
        enabled: root.chromeShown
        Behavior on opacity { NumberAnimation { duration: 250 } }

        Row {
            id: buttons
            anchors.centerIn: parent
            spacing: 0
            MIconButton {
                visible: root.video.available
                glyph: "video"
                tip: root.tr("tip.video")
                selected: root.video.showing
                onClicked: root.video.toggleVideo()
            }
            MIconButton {
                glyph: root.full ? "fullscreen_exit" : "fullscreen"
                tip: root.full ? root.tr("tip.restore") : root.tr("tip.maximize")
                onClicked: root.full ? root.restoreRequested() : root.maximizeRequested()
            }
            MIconButton {
                glyph: "expand_more"
                tip: root.tr("tip.minimize")
                onClicked: root.minimizeRequested()
            }
        }
    }

    // The playback bar is parented here by Main.qml in the maximized player.
    Item {
        id: dockHost
        visible: root.full
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.bottom: parent.bottom
        anchors.margins: 16
        height: 104
        opacity: root.chromeShown ? 1 : 0
        enabled: root.chromeShown
        Behavior on opacity { NumberAnimation { duration: 250 } }
    }
}
