import QtQuick

// A card for horizontal shelves: cover (round for artists) with a title and a supporting line under it.
// showAdd (songs): right click or the ⋮ button opens a menu to play it next or add it to the queue.
// With a videoId (songs) the menu also offers to like it. The ⋮ button sits on an almost opaque disc, so it reads on
// any cover.
Item {
    id: card

    readonly property var theme: Window.window ? Window.window.theme : null
    readonly property var i18n: Window.window ? Window.window.i18n : null

    property string heading: ""
    property string supporting: ""
    property string artUrl: ""
    property bool round: false
    property bool showAdd: false
    property string videoId: ""
    readonly property bool hasMenu: showAdd || canLike
    // A real YouTube id and a signed-in account (Main.qml decides). `liked` is read when the menu opens.
    readonly property bool canLike: {
        const w = card.Window.window
        return videoId !== "" && w && w.canLikeId ? w.canLikeId(videoId) : false
    }
    property bool liked: false
    property color cOnSurface: theme ? theme.inkSurface : "#1D1B20"
    property color cOnSurfaceVariant: theme ? theme.inkSurfaceVariant : "#49454F"

    signal activated()
    signal addRequested()
    signal playNextRequested()

    function tr(key) { return i18n ? i18n.t(key) : key }
    function toast(key) {
        if (Window.window && Window.window.toast)
            Window.window.toast(card.tr(key))
    }

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
        acceptedButtons: Qt.LeftButton | Qt.RightButton
        cursorShape: Qt.PointingHandCursor
        onClicked: (mouse) => {
            if (mouse.button === Qt.RightButton && card.hasMenu)
                menu.popup()
            else if (mouse.button === Qt.LeftButton)
                card.activated()
        }
    }

    // Under the ⋮ button: a disc of the surface color, nearly opaque, so the dots do not get lost on a busy cover.
    Rectangle {
        anchors.fill: more
        radius: height / 2
        color: card.theme ? card.theme.surfaceContainerHigh : "#ECE6F0"
        opacity: 0.96
        visible: more.visible
    }

    MIconButton {
        id: more
        visible: card.hasMenu && (area.containsMouse || hovered)
        anchors.top: art.top
        anchors.right: art.right
        anchors.margins: 4
        containerSize: 36
        glyph: "more"
        tip: card.tr("tip.more")
        cOnSurfaceVariant: card.cOnSurface
        onClicked: menu.popup(more, 0, more.height)
    }

    MMenu {
        id: menu
        onAboutToShow: card.liked = card.Window.window && card.Window.window.isLiked ? card.Window.window.isLiked(card.videoId) : false

        MMenuItem {
            glyph: "playlist_add"
            text: card.tr("menu.playNext")
            onTriggered: { card.playNextRequested(); card.toast("queue.addedNext") }
        }
        MMenuItem {
            glyph: "add"
            text: card.tr("menu.addToQueue")
            onTriggered: { card.addRequested(); card.toast("queue.added") }
        }
        MMenuItem {
            visible: card.canLike
            glyph: card.liked ? "favorite" : "favorite_border"
            text: card.liked ? card.tr("menu.unlike") : card.tr("menu.like")
            onTriggered: {
                card.toast(card.liked ? "like.removed" : "like.added")
                const w = card.Window.window
                if (w) w.toggleLikeOf(card.videoId)
            }
        }
        MMenuItem {
            visible: card.canLike
            glyph: "playlist_add"
            text: card.tr("menu.addToPlaylist")
            onTriggered: {
                const w = card.Window.window
                if (w && w.pickPlaylist) w.pickPlaylist(card.videoId)
            }
        }
    }
}
