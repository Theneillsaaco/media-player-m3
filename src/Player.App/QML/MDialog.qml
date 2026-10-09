import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic

// Material 3 basic dialog: surface container high, 28dp corners, 24dp padding, 32% scrim,
// headline-small title. Put the content (and the action buttons) inside it as children.
// Imports the Basic style explicitly so no system style can change it.
Popup {
    id: dialog

    property string title: ""
    property color cSurface: "#ECE6F0"
    property color cOnSurface: "#1D1B20"
    default property alias content: body.data

    parent: Overlay.overlay
    anchors.centerIn: parent
    width: Math.min(parent ? parent.width - 48 : 420, 440)
    modal: true
    focus: true
    padding: 24
    closePolicy: Popup.CloseOnEscape | Popup.CloseOnPressOutside

    Overlay.modal: Rectangle { color: Qt.rgba(0, 0, 0, 0.32) }

    enter: Transition { NumberAnimation { property: "opacity"; from: 0; to: 1; duration: 150 } }
    exit: Transition { NumberAnimation { property: "opacity"; from: 1; to: 0; duration: 100 } }

    background: Rectangle {
        color: dialog.cSurface
        radius: 28
    }

    contentItem: ColumnLayout {
        spacing: 16

        Text {
            Layout.fillWidth: true
            visible: dialog.title.length > 0
            text: dialog.title
            color: dialog.cOnSurface
            font.pixelSize: 24
            wrapMode: Text.Wrap
        }

        ColumnLayout {
            id: body
            Layout.fillWidth: true
            spacing: 16
        }
    }
}
