import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic
import "../Components"

// Home: the shelves of YouTube Music as one list, a title row before each shelf. Songs play (the queue becomes
// their shelf), albums, artists and playlists open their page (entityActivated). nav is the NavigationViewModel.
Item {
    id: root

    required property var nav
    readonly property var theme: Window.window ? Window.window.theme : null

    signal entityActivated(string kind, string entityId)

    ListView {
        id: list
        anchors.fill: parent
        clip: true
        boundsBehavior: Flickable.StopAtBounds
        model: root.nav.homeRows
        ScrollBar.vertical: ScrollBar {}

        delegate: Loader {
            id: del

            required property int index
            required property var model

            width: ListView.view.width
            height: item ? item.implicitHeight : 0
            sourceComponent: model.kind === "header" ? headerRow : model.kind === "track" ? trackRow : entityRow

            Component {
                id: headerRow
                Item {
                    implicitHeight: 64
                    Text {
                        anchors.left: parent.left
                        anchors.right: parent.right
                        anchors.bottom: parent.bottom
                        anchors.leftMargin: 20
                        anchors.rightMargin: 20
                        anchors.bottomMargin: 8
                        text: del.model.title
                        color: root.theme ? root.theme.onSurface : "#1D1B20"
                        font.pixelSize: 20
                        font.weight: Font.Medium
                        elide: Text.ElideRight
                    }
                }
            }

            Component {
                id: trackRow
                TrackItem {
                    width: del.width
                    heading: del.model.title
                    supporting: del.model.subtitle
                    artUrl: del.model.artUrl
                    onActivated: root.nav.playHomeRow(del.index)
                }
            }

            Component {
                id: entityRow
                TrackItem {
                    width: del.width
                    heading: del.model.title
                    supporting: del.model.subtitle
                    artUrl: del.model.artUrl
                    round: del.model.kind === "artist"
                    onActivated: root.entityActivated(del.model.kind, del.model.entityId)
                }
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
            color: root.theme ? root.theme.onSurfaceVariant : "#49454F"
            font.pixelSize: 14
        }
        MButton {
            Layout.alignment: Qt.AlignHCenter
            visible: root.nav.failed
            kind: "tonal"
            text: "Reintentar"
            onClicked: root.nav.retry()
        }
    }
}
