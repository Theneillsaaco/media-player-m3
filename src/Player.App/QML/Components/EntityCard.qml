import QtQuick

// A card for horizontal shelves: cover (round for artists) with a title and a supporting line under it.
Item {
    id: card

    readonly property var theme: Window.window ? Window.window.theme : null

    property string heading: ""
    property string supporting: ""
    property string artUrl: ""
    property bool round: false
    property color cOnSurface: theme ? theme.onSurface : "#1D1B20"
    property color cOnSurfaceVariant: theme ? theme.onSurfaceVariant : "#49454F"

    signal activated()

    width: 148
    height: 148 + 56

    Cover {
        id: art
        width: 148
        height: 148
        radius: card.round ? 74 : 12
        source: card.artUrl
        pixels: 296
        icon: card.round ? "album" : "music"
    }

    Rectangle { // state layer
        anchors.fill: art
        radius: art.radius
        color: card.cOnSurface
        opacity: area.pressed ? 0.10 : area.containsMouse ? 0.08 : 0
    }

    Column {
        anchors.top: art.bottom
        anchors.topMargin: 8
        width: card.width
        spacing: 2

        Text {
            width: parent.width
            text: card.heading
            color: card.cOnSurface
            font.pixelSize: 14
            font.weight: Font.Medium
            horizontalAlignment: card.round ? Text.AlignHCenter : Text.AlignLeft
            elide: Text.ElideRight
        }
        Text {
            width: parent.width
            text: card.supporting
            color: card.cOnSurfaceVariant
            font.pixelSize: 12
            horizontalAlignment: card.round ? Text.AlignHCenter : Text.AlignLeft
            elide: Text.ElideRight
        }
    }

    MouseArea {
        id: area
        anchors.fill: parent
        hoverEnabled: true
        cursorShape: Qt.PointingHandCursor
        onClicked: card.activated()
    }
}
