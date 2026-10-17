import QtQuick
import QtQuick.Controls.Basic

// Material 3 search bar: 56dp, full shape, leading search icon, clear button while it has text.
TextField {
    id: field

    readonly property var theme: Window.window ? Window.window.theme : null

    property color cPrimary: theme ? theme.primary : "#6750A4"
    property color cContainer: theme ? theme.surfaceContainerHigh : "#ECE6F0"
    property color cOnSurface: theme ? theme.inkSurface : "#1D1B20"
    property color cOnSurfaceVariant: theme ? theme.inkSurfaceVariant : "#49454F"

    implicitHeight: 56
    leftPadding: 56
    rightPadding: 56
    verticalAlignment: TextInput.AlignVCenter
    font.pixelSize: 16
    color: cOnSurface
    placeholderTextColor: cOnSurfaceVariant
    selectByMouse: true

    background: Rectangle {
        radius: height / 2
        color: field.cContainer
        border.width: field.activeFocus ? 2 : 0
        border.color: field.cPrimary
    }

    Icon {
        x: 16
        anchors.verticalCenter: parent.verticalCenter
        name: "search"
        tint: field.cOnSurfaceVariant
    }

    MIconButton {
        x: field.width - width - 8
        anchors.verticalCenter: parent.verticalCenter
        glyph: "close"
        visible: field.text.length > 0
        onClicked: { field.clear(); field.forceActiveFocus() }
    }
}
