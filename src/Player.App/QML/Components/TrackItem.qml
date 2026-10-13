import QtQuick
import QtQuick.Layouts

// Material 3 two-line list item with a 48dp cover. Click plays; the + button adds to the queue.
Item {
    id: row

    readonly property var theme: Window.window ? Window.window.theme : null

    property string heading: ""
    property string supporting: ""
    property string trailing: ""
    property string artUrl: ""
    property bool current: false
    property bool showAdd: false
    property color cPrimary: theme ? theme.primary : "#6750A4"
    property color cOnSurface: theme ? theme.onSurface : "#1D1B20"
    property color cOnSurfaceVariant: theme ? theme.onSurfaceVariant : "#49454F"
    property color cSecondaryContainer: theme ? theme.secondaryContainer : "#E8DEF8"
    signal activated()
    signal addRequested()

    implicitHeight: 64

    Rectangle {
        anchors.fill: parent
        anchors.leftMargin: 4
        anchors.rightMargin: 4
        radius: 12
        color: row.current ? Qt.alpha(row.cSecondaryContainer, 0.6) : "transparent"

        Rectangle { // state layer
            anchors.fill: parent
            radius: parent.radius
            color: row.cOnSurface
            opacity: area.pressed ? 0.10 : area.containsMouse ? 0.08 : 0
        }
    }

    MouseArea {
        id: area
        anchors.fill: parent
        hoverEnabled: true
        onClicked: row.activated()
    }

    RowLayout {
        anchors.fill: parent
        anchors.leftMargin: 12
        anchors.rightMargin: 8
        spacing: 12

        Rectangle { // cover (placeholder shows while it loads or if it fails)
            Layout.preferredWidth: 48
            Layout.preferredHeight: 48
            color: theme ? theme.surfaceContainerHighest : "#E6E0E9"
            Icon { anchors.centerIn: parent; name: "music"; size: 24; tint: row.cOnSurfaceVariant; visible: img.status !== Image.Ready }
            Image {
                id: img
                anchors.fill: parent
                source: row.artUrl
                asynchronous: true
                fillMode: Image.PreserveAspectCrop
                sourceSize.width: 96
                sourceSize.height: 96
            }
        }

        ColumnLayout {
            Layout.fillWidth: true
            spacing: 2
            Text {
                Layout.fillWidth: true
                text: row.heading
                color: row.current ? row.cPrimary : row.cOnSurface
                font.pixelSize: 16
                elide: Text.ElideRight
            }
            Text {
                Layout.fillWidth: true
                text: row.supporting
                color: row.cOnSurfaceVariant
                font.pixelSize: 14
                elide: Text.ElideRight
            }
        }

        Text {
            text: row.trailing
            color: row.cOnSurfaceVariant
            font.pixelSize: 12
        }

        MIconButton {
            visible: row.showAdd
            glyph: "add"
            onClicked: row.addRequested()
        }
    }
}
