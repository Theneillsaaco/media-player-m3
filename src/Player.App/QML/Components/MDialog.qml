import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic

// Material 3 basic dialog: surface container high, 28dp corners, 24dp padding, 32% scrim,
// headline-small title. Put the content (and the action buttons) inside it as children;
// it scrolls when the window is too short for it.
// Imports the Basic style explicitly so no system style can change it.
Popup {
    id: dialog

    property string title: ""
    // The window's theme through the overlay it sits on; a dialog can also be given one explicitly.
    property var theme: parent && parent.Window.window ? parent.Window.window.theme : null
    property color cSurface: theme ? theme.surfaceContainerHigh : "#ECE6F0"
    property color cOnSurface: theme ? theme.inkSurface : "#1D1B20"
    default property alias content: body.data

    parent: Overlay.overlay
    anchors.centerIn: parent
    width: Math.min(parent ? parent.width - 48 : 440, 440)
    height: Math.min(implicitHeight, parent ? parent.height - 48 : implicitHeight)
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

        Flickable {
            Layout.fillWidth: true
            Layout.fillHeight: true
            Layout.preferredHeight: body.implicitHeight
            contentWidth: width
            contentHeight: body.implicitHeight
            clip: true
            boundsBehavior: Flickable.StopAtBounds
            ScrollBar.vertical: ScrollBar {}

            ColumnLayout {
                id: body
                width: parent.width
                spacing: 16
            }
        }
    }
}
