import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic

// Material 3 two-line list item with a cover. Click plays or opens; right click or the ⋮ button opens a menu.
// showAdd: the item can be added to the queue (a + button and "play next" / "add to queue" in the menu).
// showRemove: the menu also offers "remove from queue". draggable: a grip on the left that reorders (the queue).
// With a videoId the second line is a link to the artist's page. round: circular cover (artists).
Item {
    id: row

    readonly property var theme: Window.window ? Window.window.theme : null
    readonly property var i18n: Window.window ? Window.window.i18n : null

    property string heading: ""
    property string supporting: ""
    property string trailing: ""
    property string artUrl: ""
    property string videoId: ""
    property bool current: false
    property bool showAdd: false
    property bool showRemove: false
    property bool draggable: false
    property bool dimmed: false
    property bool round: false
    property color cPrimary: theme ? theme.primary : "#6750A4"
    property color cOnSurface: theme ? theme.inkSurface : "#1D1B20"
    property color cOnSurfaceVariant: theme ? theme.inkSurfaceVariant : "#49454F"
    property color cSecondaryContainer: theme ? theme.secondaryContainer : "#E8DEF8"
    property color cOnSecondaryContainer: theme ? theme.inkSecondaryContainer : "#1D192B"

    readonly property bool linkable: videoId !== "" && supporting !== ""
    readonly property bool hasMenu: showAdd || showRemove || canLike
    // A real YouTube id and a signed-in account (Main.qml decides). `liked` is read when the menu opens.
    readonly property bool canLike: {
        const w = row.Window.window
        return videoId !== "" && w && w.canLikeId ? w.canLikeId(videoId) : false
    }
    property bool liked: false

    signal activated()
    signal addRequested()
    signal playNextRequested()
    signal removeRequested()
    // The grip of a draggable item: positions are in window coordinates.
    signal gripPressed()
    signal gripMoved(real sceneX, real sceneY)
    signal gripReleased(bool dropped)

    function tr(key) { return i18n ? i18n.t(key) : key }
    function toast(key) {
        if (Window.window && Window.window.toast)
            Window.window.toast(row.tr(key))
    }

    implicitHeight: 64
    opacity: dimmed ? 0.4 : 1

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
        acceptedButtons: Qt.LeftButton | Qt.RightButton
        cursorShape: Qt.PointingHandCursor
        onClicked: (mouse) => {
            if (mouse.button === Qt.RightButton && row.hasMenu)
                menu.popup()
            else if (mouse.button === Qt.LeftButton)
                row.activated()
        }
    }

    RowLayout {
        anchors.fill: parent
        anchors.leftMargin: row.draggable ? 8 : 16
        anchors.rightMargin: 12
        spacing: 12

        Item { // grip
            visible: row.draggable
            Layout.preferredWidth: 28
            Layout.fillHeight: true

            Icon {
                anchors.centerIn: parent
                name: "drag"
                size: 20
                tint: row.cOnSurfaceVariant
            }
            MouseArea {
                id: grip
                anchors.fill: parent
                cursorShape: pressed ? Qt.ClosedHandCursor : Qt.OpenHandCursor
                preventStealing: true
                hoverEnabled: true
                ToolTip.visible: containsMouse && !pressed
                ToolTip.text: row.tr("tip.drag")
                ToolTip.delay: 600
                onPressed: row.gripPressed()
                onPositionChanged: (mouse) => {
                    if (!pressed) return
                    const p = grip.mapToItem(null, mouse.x, mouse.y)
                    row.gripMoved(p.x, p.y)
                }
                onReleased: row.gripReleased(true)
                onCanceled: row.gripReleased(false)
            }
        }

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
                Layout.fillWidth: true
                text: row.supporting
                color: row.linkable && link.containsMouse ? row.cPrimary
                    : row.current ? row.cOnSecondaryContainer : row.cOnSurfaceVariant
                font.pixelSize: 14
                font.underline: row.linkable && link.containsMouse
                elide: Text.ElideRight

                MouseArea {
                    id: link
                    width: Math.min(sub.implicitWidth, sub.width)
                    height: sub.height
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
            tip: row.tr("tip.add")
            onClicked: { row.addRequested(); row.toast("queue.added") }
        }

        MIconButton {
            id: more
            visible: row.hasMenu
            glyph: "more"
            tip: row.tr("tip.more")
            onClicked: menu.popup(more, 0, more.height)
        }
    }

    MMenu {
        id: menu
        onAboutToShow: row.liked = row.Window.window && row.Window.window.isLiked ? row.Window.window.isLiked(row.videoId) : false

        MMenuItem {
            visible: row.showAdd
            glyph: "playlist_add"
            text: row.tr("menu.playNext")
            onTriggered: { row.playNextRequested(); row.toast("queue.addedNext") }
        }
        MMenuItem {
            visible: row.showAdd
            glyph: "add"
            text: row.tr("menu.addToQueue")
            onTriggered: { row.addRequested(); row.toast("queue.added") }
        }
        MMenuItem {
            visible: row.canLike
            glyph: row.liked ? "favorite" : "favorite_border"
            text: row.liked ? row.tr("menu.unlike") : row.tr("menu.like")
            onTriggered: {
                row.toast(row.liked ? "like.removed" : "like.added")
                const w = row.Window.window
                if (w) w.toggleLikeOf(row.videoId)
            }
        }
        MMenuItem {
            visible: row.canLike
            glyph: "playlist_add"
            text: row.tr("menu.addToPlaylist")
            onTriggered: {
                const w = row.Window.window
                if (w && w.pickPlaylist) w.pickPlaylist(row.videoId)
            }
        }

        MMenuItem {
            visible: row.showRemove
            glyph: "delete"
            text: row.tr("menu.removeFromQueue")
            onTriggered: row.removeRequested()
        }
    }
}
