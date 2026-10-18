import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic
import "../Components"

// Home, laid out like YouTube Music on the web: one block per shelf with its title and two arrows. A shelf of
// songs only ("Quick picks") is a grid of four rows that scrolls sideways; every other shelf is a strip of cards.
// Songs play (the queue becomes their shelf); albums, artists and playlists open their page (entityActivated).
// nav is the NavigationViewModel; its homeJson is [{title, layout, items:[{kind, id, index, title, subtitle, art}]}].
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
        topMargin: 8
        bottomMargin: 24
        model: root.shelves
        ScrollBar.vertical: ScrollBar {}

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
                anchors.leftMargin: 20
                anchors.rightMargin: 12
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
                leftMargin: 20
                rightMargin: 20
                boundsBehavior: Flickable.StopAtBounds
                model: shelf.isGrid ? [] : shelf.modelData.items

                delegate: EntityCard {
                    required property var modelData

                    heading: modelData.title
                    supporting: modelData.subtitle
                    artUrl: modelData.art
                    round: modelData.kind === "artist"
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
                leftMargin: 12
                rightMargin: 12
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
