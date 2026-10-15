import QtQuick

// Cover art with rounded corners and a placeholder while it loads. The rounding is a mask from QtQuick.Effects,
// loaded on demand (CoverMask.qml): a Qt without it only loses the rounding, the cover stays square.
Item {
    id: root

    readonly property var theme: Window.window ? Window.window.theme : null

    property string source: ""
    property real radius: 8
    property int pixels: 96
    property string icon: "music"

    implicitWidth: 48
    implicitHeight: 48

    Rectangle {
        anchors.fill: parent
        radius: root.radius
        color: root.theme ? root.theme.surfaceContainerHighest : "#E6E0E9"
    }

    Icon {
        anchors.centerIn: parent
        name: root.icon
        size: Math.max(16, Math.min(40, root.width * 0.4))
        tint: root.theme ? root.theme.onSurfaceVariant : "#49454F"
        visible: img.status !== Image.Ready
    }

    Image {
        id: img
        anchors.fill: parent
        source: root.source
        asynchronous: true
        fillMode: Image.PreserveAspectCrop
        sourceSize.width: root.pixels
        sourceSize.height: root.pixels
        visible: mask.status !== Loader.Ready
    }

    Loader {
        id: mask
        anchors.fill: parent
        active: img.status === Image.Ready && root.radius > 0
        source: "CoverMask.qml"
        onLoaded: {
            item.target = img
            item.radius = Qt.binding(() => root.radius)
        }
    }
}
