import QtQuick
import QtQuick.Layouts

// Material 3 two-line list item with a cover. Click plays or opens; the + button adds to the queue.
// With a videoId the second line is a link to the artist's page. round: circular cover (artists).
Item {
    id: row

    readonly property var theme: Window.window ? Window.window.theme : null

    property string heading: ""
    property string supporting: ""
    property string trailing: ""
    property string artUrl: ""
    property string videoId: ""
    property bool current: false
    property bool showAdd: false
    property bool round: false
    property color cPrimary: theme ? theme.primary : "#6750A4"
    property color cOnSurface: theme ? theme.onSurface : "#1D1B20"
    property color cOnSurfaceVariant: theme ? theme.onSurfaceVariant : "#49454F"
    property color cSecondaryContainer: theme ? theme.secondaryContainer : "#E8DEF8"
    property color cOnSecondaryContainer: theme ? theme.onSecondaryContainer : "#1D192B"

    readonly property bool linkable: videoId !== "" && supporting !== ""

    signal activated()
    signal addRequested()

    implicitHeight: 64

    Rectangle {
        anchors.fill: parent
        anchors.leftMargin: 8
        anchors.rightMargin: 8
        anchors.topMargin: 2
        anchors.bottomMargin: 2
        radius: 14
        color: row.current ? row.cSecondaryContainer : "transparent"

        Rectangle { // state layer
            anchors.fill: parent
            radius: parent.radius
            color: row.current ? row.cOnSecondaryContainer : row.cOnSurface
            opacity: area.pressed ? 0.10 : area.containsMouse ? 0.08 : 0
        }
    }

    MouseArea {
        id: area
        anchors.fill: parent
        hoverEnabled: true
        cursorShape: Qt.PointingHandCursor
        onClicked: row.activated()
    }

    RowLayout {
        anchors.fill: parent
        anchors.leftMargin: 16
        anchors.rightMargin: 12
        spacing: 12

        Cover {
            Layout.preferredWidth: 48
            Layout.preferredHeight: 48
            radius: row.round ? 24 : 8
            source: row.artUrl
        }

        ColumnLayout {
            id: texts
            Layout.fillWidth: true
            spacing: 2

            Text {
                Layout.fillWidth: true
                text: row.heading
                color: row.current ? row.cOnSecondaryContainer : row.cOnSurface
                font.pixelSize: 16
                font.weight: row.current ? Font.Medium : Font.Normal
                elide: Text.ElideRight
            }

            Text {
                id: sub
                Layout.alignment: Qt.AlignLeft
                Layout.maximumWidth: texts.width
                text: row.supporting
                color: row.linkable && link.containsMouse ? row.cPrimary
                    : row.current ? row.cOnSecondaryContainer : row.cOnSurfaceVariant
                font.pixelSize: 14
                font.underline: row.linkable && link.containsMouse
                elide: Text.ElideRight

                MouseArea {
                    id: link
                    anchors.fill: parent
                    enabled: row.linkable
                    hoverEnabled: true
                    cursorShape: row.linkable ? Qt.PointingHandCursor : Qt.ArrowCursor
                    onClicked: {
                        if (Window.window && Window.window.openArtistOf)
                            Window.window.openArtistOf(row.videoId)
                    }
                }
            }
        }

        Text {
            visible: row.trailing !== ""
            Layout.preferredWidth: 44
            horizontalAlignment: Text.AlignRight
            text: row.trailing
            color: row.current ? row.cOnSecondaryContainer : row.cOnSurfaceVariant
            font.pixelSize: 12
        }

        MIconButton {
            visible: row.showAdd
            glyph: "add"
            onClicked: row.addRequested()
        }
    }
}
