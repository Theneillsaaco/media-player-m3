import QtQuick
import QtQuick.Effects

// Draws `target` (an Image) through a rounded-rectangle mask. Loaded by Cover.qml only when it is needed.
Item {
    id: root

    property Item target: null
    property real radius: 8

    Rectangle {
        id: shape
        anchors.fill: parent
        radius: root.radius
        layer.enabled: true
        visible: false
    }

    MultiEffect {
        anchors.fill: parent
        source: root.target
        maskEnabled: true
        maskSource: shape
    }
}
