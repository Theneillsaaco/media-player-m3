import QtQuick
import QtQuick.Controls.Basic

// Material 3 icon button: 40dp container, 24dp icon. State layers: hover 8%, focus/pressed 10%.
// filled: primary container (play/pause). selected: tonal container (toggle that is on).
// Imports the Basic style explicitly so no system style can change it.
Button {
    id: control

    readonly property var theme: Window.window ? Window.window.theme : null

    property string glyph: "play"
    property bool selected: false
    property bool filled: false
    property real containerSize: 40
    property color cPrimary: theme ? theme.primary : "#6750A4"
    property color cOnPrimary: theme ? theme.onPrimary : "#FFFFFF"
    property color cSecondaryContainer: theme ? theme.secondaryContainer : "#E8DEF8"
    property color cOnSecondaryContainer: theme ? theme.onSecondaryContainer : "#1D192B"
    property color cOnSurface: theme ? theme.onSurface : "#1D1B20"
    property color cOnSurfaceVariant: theme ? theme.onSurfaceVariant : "#49454F"

    readonly property color content: filled ? cOnPrimary
        : selected ? cOnSecondaryContainer : cOnSurfaceVariant

    implicitWidth: containerSize
    implicitHeight: containerSize
    padding: 0
    hoverEnabled: true
    focusPolicy: Qt.StrongFocus

    contentItem: Item {
        Icon {
            anchors.centerIn: parent
            name: control.glyph
            size: 24
            tint: control.enabled ? control.content : Qt.alpha(control.cOnSurface, 0.38)
        }
    }

    background: Rectangle {
        radius: height / 2
        color: !control.enabled ? (control.filled ? Qt.alpha(control.cOnSurface, 0.12) : "transparent")
            : control.filled ? control.cPrimary
                : control.selected ? control.cSecondaryContainer : "transparent"

        Rectangle { // state layer
            anchors.fill: parent
            radius: parent.radius
            color: control.content
            opacity: !control.enabled ? 0
                : control.down ? 0.10
                    : control.visualFocus ? 0.10
                        : control.hovered ? 0.08 : 0
        }
        Rectangle { // focus ring
            anchors.fill: parent
            anchors.margins: -5
            radius: height / 2
            color: "transparent"
            border.width: 3
            border.color: control.cPrimary
            visible: control.visualFocus
        }
    }
}
