import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic
import "../Components"

// Home, laid out like YouTube Music on the web: one block per shelf with its title and two arrows. A shelf of
// songs only ("Quick picks") is a grid of four rows that scrolls sideways; every other shelf is a strip of cards.
// Songs play (the queue becomes their shelf); albums, artists and playlists open their page (entityActivated).
// nav is the NavigationViewModel; its homeJson is [{title, layout, items:[{kind, id, index, title, subtitle, art}]}].
// The list fills the whole card (the card and its corner mask do the rounded clipping) and the breathing room is
// margin inside the lists, so nothing is cut in a square a few pixels inside the card.
Item {
    id: root

    required property var nav
    readonly property var theme: Window.window ? Window.window.theme : null
    readonly property var i18n: Window.window ? Window.window.i18n : null
    readonly property var shelves: nav.homeJson.length > 0 ? JSON.parse(nav.homeJson) : []

    signal entityActivated(string kind, string entityId)

    function tr(key) { return i18n ? i18n.t(key) : key }

    ListView {
        id: list
        anchors.fill: parent
        clip: true
        boundsBehavior: Flickable.StopAtBounds
        topMargin: 10
        bottomMargin: 20
        model: root.shelves
        ScrollBar.vertical: MScrollBar {}

        // The shelves come in one after the other when Home is filled.
        populate: Transition {
            SequentialAnimation {
                PropertyAction { property: "opacity"; value: 0 }
                PauseAnimation { duration: Math.min(ViewTransition.index, 5) * 60 }
                ParallelAnimation {
                    Anim { property: "opacity"; to: 1; type: Anim.DefaultEffects }
                    Anim { property: "y"; from: ViewTransition.destination.y + 16; type: Anim.DefaultSpatial }
                }
            }
        }

        delegate: Item {
            id: shelf

            required property int index
            required property var modelData

            readonly property bool isGrid: modelData.layout === "grid"
            // The view that scrolls: the arrows move it.
            readonly property Item view: isGrid ? gridView : strip

            width: ListView.view.width
            height: head.height + (isGrid ? gridView.height : strip.height) + 20

            function slide(dx) {
                const v = shelf.view
                const low = v.originX - v.leftMargin
                const high = v.originX + v.contentWidth - v.width + v.rightMargin
                scroller.stop()
                scroller.from = v.contentX
                scroller.to = Math.max(low, Math.min(high, v.contentX + dx))
                scroller.start()
            }

            RowLayout {
                id: head
                anchors.top: parent.top
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.leftMargin: 24
                anchors.rightMargin: 16
                height: 64
                spacing: 4

                Text {
                    Layout.fillWidth: true
                    text: shelf.modelData.title
                    color: root.theme ? root.theme.inkSurface : "#1D1B20"
                    font.pixelSize: 24
                    font.weight: Font.Medium
                    elide: Text.ElideRight
                }
                MIconButton {
                    glyph: "back"
                    enabled: !shelf.view.atXBeginning
                    onClicked: shelf.slide(-shelf.view.width * 0.8)
                }
                MIconButton {
                    glyph: "back"
                    rotation: 180
                    enabled: !shelf.view.atXEnd
                    onClicked: shelf.slide(shelf.view.width * 0.8)
                }
            }

            // Strip of cards
            ListView {
                id: strip
                visible: !shelf.isGrid
                anchors.top: head.bottom
                anchors.left: parent.left
                anchors.right: parent.right
                height: 148 + 60
                orientation: ListView.Horizontal
                clip: true
                spacing: 16
                leftMargin: 24
                rightMargin: 24
                boundsBehavior: Flickable.StopAtBounds
                model: shelf.isGrid ? [] : shelf.modelData.items

                delegate: EntityCard {
                    required property var modelData

                    heading: modelData.title
                    supporting: modelData.subtitle
                    artUrl: modelData.art
                    round: modelData.kind === "artist"
                    showAdd: modelData.kind === "track"
                    videoId: modelData.kind === "track" ? modelData.id : ""
                    onAddRequested: root.nav.addHomeTrack(shelf.index, modelData.index)
                    onPlayNextRequested: root.nav.playNextHomeTrack(shelf.index, modelData.index)
                    onActivated: {
                        if (modelData.kind === "track")
                            root.nav.playHomeTrack(shelf.index, modelData.index)
                        else
                            root.entityActivated(modelData.kind, modelData.id)
                    }
                }
            }

            // Grid of song rows: four rows, columns fill from the top and scroll sideways.
            GridView {
                id: gridView
                visible: shelf.isGrid
                anchors.top: head.bottom
                anchors.left: parent.left
                anchors.right: parent.right
                height: 4 * cellHeight
                flow: GridView.FlowTopToBottom
                cellWidth: Math.min(440, Math.max(300, (width - 40) / 2))
                cellHeight: 64
                leftMargin: 16
                rightMargin: 16
                clip: true
                boundsBehavior: Flickable.StopAtBounds
                model: shelf.isGrid ? shelf.modelData.items : []

                delegate: TrackItem {
                    required property var modelData

                    width: gridView.cellWidth
                    height: gridView.cellHeight
                    heading: modelData.title
                    supporting: modelData.subtitle
                    artUrl: modelData.art
                    videoId: modelData.id
                    showAdd: true
                    onActivated: root.nav.playHomeTrack(shelf.index, modelData.index)
                    onAddRequested: root.nav.addHomeTrack(shelf.index, modelData.index)
                    onPlayNextRequested: root.nav.playNextHomeTrack(shelf.index, modelData.index)
                }
            }

            NumberAnimation {
                id: scroller
                target: shelf.view
                property: "contentX"
                duration: 280
                easing.type: Easing.OutCubic
            }
        }
    }

    ColumnLayout {
        anchors.centerIn: parent
        width: parent.width - 48
        visible: list.count === 0
        spacing: 16

        LoadingIndicator {
            Layout.alignment: Qt.AlignHCenter
            visible: root.nav.working && !root.nav.failed
        }

        Text {
            Layout.fillWidth: true
            horizontalAlignment: Text.AlignHCenter
            wrapMode: Text.Wrap
            text: root.nav.status
            color: root.theme ? root.theme.inkSurfaceVariant : "#49454F"
            font.pixelSize: 14
        }
        MButton {
            Layout.alignment: Qt.AlignHCenter
            visible: root.nav.failed
            kind: "tonal"
            text: root.tr("action.retry")
            onClicked: root.nav.retry()
        }
    }
}
