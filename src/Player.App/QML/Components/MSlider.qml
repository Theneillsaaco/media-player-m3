import QtQuick

// Material 3 style slider: 4dp track, 4x20dp handle, drag or click anywhere.
// value is driven from outside. changedByUser(v) fires on release, or on every move when live.
Item {
    id: root

    readonly property var theme: Window.window ? Window.window.theme : null

    property real from: 0
    property real to: 1
    property real value: 0
    property bool live: false
    property color cPrimary: theme ? theme.primary : "#6750A4"
    property color cSecondaryContainer: theme ? theme.surfaceContainerHighest : "#E6E0E9"
    property color cOnSurface: theme ? theme.onSurface : "#1D1B20"
    signal changedByUser(real v)

    readonly property bool pressed: area.pressed
    property real dragValue: 0
    readonly property real shown: area.pressed ? dragValue : value
    readonly property real fraction: to > from ? Math.max(0, Math.min(1, (shown - from) / (to - from))) : 0

    implicitHeight: 24
    implicitWidth: 120
    opacity: enabled ? 1 : 0.38

    Rectangle {
        anchors.verticalCenter: parent.verticalCenter
        width: parent.width
        height: 4
        radius: 2
        color: root.cSecondaryContainer
        Rectangle {
            width: parent.width * root.fraction
            height: parent.height
            radius: 2
            color: root.cPrimary
        }
    }
    Rectangle { // handle
        anchors.verticalCenter: parent.verticalCenter
        x: (root.width - width) * root.fraction
        width: 4
        height: area.pressed ? 24 : 20
        radius: 2
        color: root.cPrimary
    }
    MouseArea {
        id: area
        anchors.fill: parent
        enabled: root.enabled
        function valueAt(x) {
            return root.from + Math.max(0, Math.min(1, x / width)) * (root.to - root.from)
        }
        onPressed: (m) => {
            root.dragValue = valueAt(m.x)
            if (root.live) root.changedByUser(root.dragValue)
        }
        onPositionChanged: (m) => {
            if (!pressed) return
            root.dragValue = valueAt(m.x)
            if (root.live) root.changedByUser(root.dragValue)
        }
        onReleased: { if (!root.live) root.changedByUser(root.dragValue) }
    }
}
