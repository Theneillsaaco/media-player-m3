import QtQuick
import QtQuick.Controls.Basic

// Material 3 switch: 52x32 track, handle 16dp off / 24dp on / 28dp pressed, label on the left.
// State layer: 40dp circle behind the handle, hover 8%, pressed 10%.
// Imports the Basic style explicitly so no system style can change it.
Switch {
    id: control

    readonly property var theme: Window.window ? Window.window.theme : null

    property color cPrimary: theme ? theme.primary : "#6750A4"
    property color cOnPrimary: theme ? theme.inkPrimary : "#FFFFFF"
    property color cTrackOff: theme ? theme.surfaceContainerHighest : "#E6E0E9"
    property color cOutline: theme ? theme.outline : "#79747E"
    property color cOnSurface: theme ? theme.inkSurface : "#1D1B20"

    implicitHeight: 48
    implicitWidth: contentItem.implicitWidth + 16 + 52
    padding: 0
    spacing: 16
    hoverEnabled: true
    focusPolicy: Qt.StrongFocus
    opacity: enabled ? 1 : 0.38

    contentItem: Text {
        text: control.text
        font.pixelSize: 16
        color: control.cOnSurface
        verticalAlignment: Text.AlignVCenter
        elide: Text.ElideRight
    }

    indicator: Rectangle {
        x: control.width - width
        y: (control.height - height) / 2
        width: 52
        height: 32
        radius: 16
        color: control.checked ? control.cPrimary : control.cTrackOff
        border.width: control.checked ? 0 : 2
        border.color: control.cOutline

        Rectangle { // focus ring
            anchors.fill: parent
            anchors.margins: -5
            radius: height / 2
            color: "transparent"
            border.width: 3
            border.color: control.cPrimary
            visible: control.visualFocus
        }

        Rectangle { // state layer
            width: 40
            height: 40
            radius: 20
            x: handle.x + handle.width / 2 - 20
            y: 16 - 20
            color: control.checked ? control.cPrimary : control.cOnSurface
            opacity: control.down ? 0.10 : control.hovered ? 0.08 : 0
        }

        Rectangle {
            id: handle
            readonly property real size: control.down ? 28 : control.checked ? 24 : 16
            width: size
            height: size
            radius: size / 2
            y: 16 - size / 2
            x: control.checked ? 52 - 4 - 12 - size / 2 : 16 - size / 2 + (control.down ? 0 : 0)
            color: control.checked ? control.cOnPrimary : control.cOutline

            Behavior on x { NumberAnimation { duration: 150; easing.type: Easing.OutCubic } }
            Behavior on width { NumberAnimation { duration: 100 } }
            Behavior on height { NumberAnimation { duration: 100 } }
        }
    }
}
