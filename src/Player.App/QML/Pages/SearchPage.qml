import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic
import "../Components"

// Search results. vm is the SearchViewModel. The chips choose what is searched for; songs and videos play,
// albums, artists and playlists open their page (entityActivated). The status line shows while the list is empty.
Item {
    id: root

    required property var vm
    readonly property var theme: Window.window ? Window.window.theme : null

    signal entityActivated(string kind, string entityId)

    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        Flow {
            Layout.fillWidth: true
            Layout.margins: 16
            Layout.bottomMargin: 8
            spacing: 8

            Repeater {
                model: [
                    { key: "songs", label: "Canciones" },
                    { key: "videos", label: "Videos" },
                    { key: "albums", label: "Álbumes" },
                    { key: "artists", label: "Artistas" },
                    { key: "playlists", label: "Listas" }
                ]
                MChip {
                    required property var modelData
                    text: modelData.label
                    selected: root.vm.filter === modelData.key
                    onClicked: root.vm.chooseFilter(modelData.key)
                }
            }
        }

        Item {
            Layout.fillWidth: true
            Layout.fillHeight: true

            ListView {
                id: tracksView
                anchors.fill: parent
                visible: !root.vm.entityMode
                clip: true
                boundsBehavior: Flickable.StopAtBounds
                model: root.vm.results
                ScrollBar.vertical: ScrollBar {}
                delegate: TrackItem {
                    required property int index
                    required property var model
                    width: ListView.view.width
                    heading: model.title
                    supporting: model.artist
                    trailing: model.duration
                    artUrl: model.artUrl
                    videoId: model.videoId
                    showAdd: true
                    onActivated: root.vm.playResult(index)
                    onAddRequested: root.vm.addResult(index)
                }
            }

            ListView {
                id: entitiesView
                anchors.fill: parent
                visible: root.vm.entityMode
                clip: true
                boundsBehavior: Flickable.StopAtBounds
                model: root.vm.entities
                ScrollBar.vertical: ScrollBar {}
                delegate: TrackItem {
                    required property int index
                    required property var model
                    width: ListView.view.width
                    heading: model.title
                    supporting: model.subtitle
                    artUrl: model.artUrl
                    round: model.kind === "artist"
                    onActivated: root.entityActivated(model.kind, model.entityId)
                }
            }

            Text {
                anchors.centerIn: parent
                width: parent.width - 48
                horizontalAlignment: Text.AlignHCenter
                wrapMode: Text.Wrap
                visible: (root.vm.entityMode ? entitiesView.count : tracksView.count) === 0
                text: root.vm.status
                color: root.theme ? root.theme.inkSurfaceVariant : "#49454F"
                font.pixelSize: 14
            }
        }
    }
}
