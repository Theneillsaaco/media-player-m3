import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic

// Material 3 navigation rail: 80dp wide, icon in a 56x32 pill (secondary container when active) and a label.
// current is the key of the active destination; chosen(key) fires on a click.
Item {
    id: rail

    readonly property var theme: Window.window ? Window.window.theme : null
    readonly property var i18n: Window.window ? Window.window.i18n : null

    property string current: ""
    property color cActive: theme ? theme.secondaryContainer : "#E8DEF8"
    property color cOnActive: theme ? theme.inkSecondaryContainer : "#1D192B"
    property color cOnSurface: theme ? theme.inkSurface : "#1D1B20"
    property color cOnSurfaceVariant: theme ? theme.inkSurfaceVariant : "#49454F"
    property color cPrimary: theme ? theme.primary : "#6750A4"

    signal chosen(string key)

    function tr(key) { return i18n ? i18n.t(key) : key }

    implicitWidth: 80

    readonly property var destinations: [
        { key: "home", glyph: "home", label: "nav.home" },
        { key: "search", glyph: "search", label: "nav.search" },
        { key: "library", glyph: "library", label: "nav.library" }
    ]

    ColumnLayout {
        anchors.top: parent.top
        anchors.topMargin: 8
        width: parent.width
        spacing: 12

        Repeater {
            model: rail.destinations

            delegate: AbstractButton {
                id: dest

                required property var modelData
                readonly property bool active: rail.current === modelData.key

                Layout.fillWidth: true
                implicitHeight: 64
                hoverEnabled: true
                focusPolicy: Qt.StrongFocus
                onClicked: rail.chosen(modelData.key)

                background: null

                contentItem: ColumnLayout {
                    spacing: 4

                    Rectangle {
                        Layout.alignment: Qt.AlignHCenter
                        Layout.preferredWidth: 56
                        Layout.preferredHeight: 32
                        radius: 16
                        color: dest.active ? rail.cActive : "transparent"

                        Rectangle { // state layer
                            anchors.fill: parent
                            radius: parent.radius
                            color: rail.cOnSurface
                            opacity: dest.active ? 0 : dest.down ? 0.10 : dest.visualFocus ? 0.10 : dest.hovered ? 0.08 : 0
                        }

                        Icon {
                            anchors.centerIn: parent
                            name: dest.modelData.glyph
                            size: 24
                            tint: dest.active ? rail.cOnActive : rail.cOnSurfaceVariant
                        }
                    }

                    Text {
                        Layout.alignment: Qt.AlignHCenter
                        text: rail.tr(dest.modelData.label)
                        font.pixelSize: 12
                        font.weight: dest.active ? Font.DemiBold : Font.Medium
                        color: dest.active ? rail.cOnSurface : rail.cOnSurfaceVariant
                    }
                }
            }
        }
    }
}
