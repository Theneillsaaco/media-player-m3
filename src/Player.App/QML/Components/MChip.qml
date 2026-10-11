import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic
import ".."

// Material 3 filter chip: 32dp, 8dp corners, outlined; selected = secondary container + check.
// State layers: hover 8%, focus/pressed 10%. Imports the Basic style explicitly.
Button {
    id: control

    property bool selected: false
    property color cPrimary: "#6750A4"
    property color cSecondaryContainer: "#E8DEF8"
    property color cOnSecondaryContainer: "#1D192B"
    property color cOutline: "#79747E"
    property color cOnSurfaceVariant: "#49454F"

    readonly property color content: selected ? cOnSecondaryContainer : cOnSurfaceVariant

    implicitHeight: 32
    leftPadding: selected ? 8 : 16
    rightPadding: 16
    hoverEnabled: true
    focusPolicy: Qt.StrongFocus

    contentItem: RowLayout {
        spacing: 8
        Icon {
            visible: control.selected
            name: "check"
            size: 18
            tint: control.content
        }
        Text {
            Layout.alignment: Qt.AlignVCenter
            text: control.text
            font.pixelSize: 14
            font.weight: Font.Medium
            color: control.content
        }
    }

    background: Rectangle {
        radius: 8
        color: control.selected ? control.cSecondaryContainer : "transparent"
        border.width: control.selected ? 0 : 1
        border.color: control.cOutline

        Rectangle { // state layer
            anchors.fill: parent
            radius: parent.radius
            color: control.content
            opacity: control.down ? 0.10 : control.visualFocus ? 0.10 : control.hovered ? 0.08 : 0
        }
    }
}
