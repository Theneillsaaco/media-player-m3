import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic
import "../Components"

// An artist: round photo and name, Reproducir / Aleatorio, their top songs and horizontal shelves of albums,
// singles and related artists (entityActivated opens them). nav is the NavigationViewModel.
Item {
    id: root

    required property var nav
    readonly property var theme: Window.window ? Window.window.theme : null

    signal entityActivated(string kind, string entityId)

    component SectionTitle: Text {
        Layout.leftMargin: 20
        Layout.topMargin: 12
        color: root.theme ? root.theme.inkSurface : "#1D1B20"
        font.pixelSize: 20
        font.weight: Font.Medium
    }

    // A horizontal shelf of cards; it hides itself when its model is empty.
    component Shelf: ColumnLayout {
        id: shelf

        property string heading: ""
        property var shelfModel: null
        property bool round: false

        signal activated(string kind, string entityId)

        Layout.fillWidth: true
        visible: strip.count > 0
        spacing: 12

        SectionTitle { text: shelf.heading }

        ListView {
            id: strip
            Layout.fillWidth: true
            Layout.preferredHeight: 148 + 56 + 4
            orientation: ListView.Horizontal
            clip: true
            spacing: 16
            leftMargin: 20
            rightMargin: 20
            boundsBehavior: Flickable.StopAtBounds
            model: shelf.shelfModel

            delegate: EntityCard {
                required property var model
                heading: model.title
                supporting: model.subtitle
                artUrl: model.artUrl
                round: shelf.round
                onActivated: shelf.activated(model.kind, model.entityId)
            }
        }
    }

    Flickable {
        id: flick
        anchors.fill: parent
        clip: true
        contentWidth: width
        contentHeight: column.implicitHeight + 32
        boundsBehavior: Flickable.StopAtBounds
        ScrollBar.vertical: ScrollBar {}

        ColumnLayout {
            id: column
            width: flick.width
            spacing: 8

            // Header
            RowLayout {
                Layout.fillWidth: true
                Layout.margins: 20
                spacing: 20
                visible: root.nav.artistName !== ""

                Cover {
                    Layout.preferredWidth: 136
                    Layout.preferredHeight: 136
                    radius: 68
                    source: root.nav.artistArt
                    pixels: 272
                    icon: "album"
                }

                ColumnLayout {
                    Layout.fillWidth: true
                    Layout.alignment: Qt.AlignVCenter
                    spacing: 8

                    Text {
                        Layout.fillWidth: true
                        text: root.nav.artistName
                        color: root.theme ? root.theme.inkSurface : "#1D1B20"
                        font.pixelSize: 32
                        font.weight: Font.Medium
                        elide: Text.ElideRight
                    }

                    RowLayout {
                        spacing: 8
                        MButton {
                            text: "Reproducir"
                            enabled: songs.count > 0
                            onClicked: root.nav.playArtist(false)
                        }
                        MButton {
                            kind: "tonal"
                            text: "Aleatorio"
                            enabled: songs.count > 0
                            onClicked: root.nav.playArtist(true)
                        }
                    }
                }
            }

            Text {
                Layout.fillWidth: true
                Layout.leftMargin: 20
                Layout.rightMargin: 20
                visible: root.nav.artistDescription !== ""
                text: root.nav.artistDescription
                color: root.theme ? root.theme.inkSurfaceVariant : "#49454F"
                font.pixelSize: 13
                wrapMode: Text.Wrap
                maximumLineCount: 3
                elide: Text.ElideRight
            }

            SectionTitle { visible: songs.count > 0; text: "Canciones" }

            Repeater {
                id: songs
                model: root.nav.artistSongs

                delegate: TrackItem {
                    required property int index
                    required property var model
                    Layout.fillWidth: true
                    heading: model.title
                    supporting: model.artist
                    trailing: model.duration
                    artUrl: model.artUrl
                    videoId: model.videoId
                    showAdd: true
                    onActivated: root.nav.playArtistSong(index)
                    onAddRequested: root.nav.addArtistSong(index)
                }
            }

            Shelf {
                heading: "Álbumes"
                shelfModel: root.nav.artistAlbums
                onActivated: (kind, entityId) => root.entityActivated(kind, entityId)
            }
            Shelf {
                heading: "Sencillos y EP"
                shelfModel: root.nav.artistSingles
                onActivated: (kind, entityId) => root.entityActivated(kind, entityId)
            }
            Shelf {
                heading: "Artistas similares"
                shelfModel: root.nav.artistRelated
                round: true
                onActivated: (kind, entityId) => root.entityActivated(kind, entityId)
            }
        }
    }

    // Loading or error, while there is nothing to show yet.
    ColumnLayout {
        anchors.centerIn: parent
        width: parent.width - 48
        visible: root.nav.artistName === "" && root.nav.status !== ""
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
            text: "Reintentar"
            onClicked: root.nav.retry()
        }
    }
}
