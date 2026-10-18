import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic

// One entry of an MMenu: optional icon and a label. State layer: hover 8%, pressed 10%.
MenuItem {
    id: item

    readonly property var theme: Window.window ? Window.window.theme : null

    property string glyph: ""
    property color cOnSurface: theme ? theme.inkSurface : "#1D1B20"
    property color cOnSurfaceVariant: theme ? theme.inkSurfaceVariant : "#49454F"

    implicitWidth: 232
    implicitHeight: visible ? 48 : 0
    height: visible ? implicitHeight : 0
    padding: 0
    hoverEnabled: true

    contentItem: RowLayout {
        spacing: 12

        Item { Layout.preferredWidth: 4 }
        Icon {
            visible: item.glyph.length > 0
            name: item.glyph
            size: 20
            tint: item.cOnSurfaceVariant
        }
        Text {
            Layout.fillWidth: true
            text: item.text
            color: item.cOnSurface
            font.pixelSize: 14
            verticalAlignment: Text.AlignVCenter
            elide: Text.ElideRight
        }
    }

    background: Rectangle {
        radius: 8
        color: "transparent"

        Rectangle { // state layer
            anchors.fill: parent
            radius: parent.radius
            color: item.cOnSurface
            opacity: item.down ? 0.10 : item.highlighted || item.hovered ? 0.08 : 0
        }
    }
}
