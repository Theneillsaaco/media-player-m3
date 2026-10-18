import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic
import "../Components"

// An album, a playlist or a mix: cover, title, who made it (a link to the artist when there is one), the
// Reproducir / Aleatorio buttons and its tracks. nav is the NavigationViewModel; Back is in the top bar.
Item {
    id: root

    required property var nav
    readonly property var theme: Window.window ? Window.window.theme : null
    readonly property var i18n: Window.window ? Window.window.i18n : null

    function tr(key) { return i18n ? i18n.t(key) : key }

    ListView {
        id: list
        anchors.fill: parent
        clip: true
        boundsBehavior: Flickable.StopAtBounds
        model: root.nav.collection
        ScrollBar.vertical: ScrollBar {}

        header: Item {
            width: list.width
            height: root.nav.collectionTitle !== "" ? 208 : 0
            visible: height > 0

            RowLayout {
                anchors.fill: parent
                anchors.margins: 20
                spacing: 20

                Cover {
                    Layout.preferredWidth: 168
                    Layout.preferredHeight: 168
                    radius: 16
                    source: root.nav.collectionArt
                    pixels: 336
                }

                ColumnLayout {
                    id: details
                    Layout.fillWidth: true
                    Layout.alignment: Qt.AlignVCenter
                    spacing: 6

                    Text {
                        Layout.fillWidth: true
                        text: root.nav.collectionTitle
                        color: root.theme ? root.theme.inkSurface : "#1D1B20"
                        font.pixelSize: 28
                        font.weight: Font.Medium
                        wrapMode: Text.Wrap
                        maximumLineCount: 2
                        elide: Text.ElideRight
                    }

                    Text {
                        Layout.alignment: Qt.AlignLeft
                        Layout.maximumWidth: details.width
                        text: root.nav.collectionSubtitle
                        color: subLink.containsMouse && root.nav.canOpenCollectionArtist
                            ? (root.theme ? root.theme.primary : "#6750A4")
                            : (root.theme ? root.theme.inkSurfaceVariant : "#49454F")
                        font.pixelSize: 14
                        font.underline: subLink.containsMouse && root.nav.canOpenCollectionArtist
                        elide: Text.ElideRight

                        MouseArea {
                            id: subLink
                            anchors.fill: parent
                            enabled: root.nav.canOpenCollectionArtist
                            hoverEnabled: true
                            cursorShape: enabled ? Qt.PointingHandCursor : Qt.ArrowCursor
                            onClicked: root.nav.openCollectionArtist()
                        }
                    }

                    RowLayout {
                        Layout.topMargin: 8
                        spacing: 8

                        MButton {
                            text: root.tr("action.play")
                            enabled: root.nav.collectionCount > 0
                            onClicked: root.nav.playCollection(false)
                        }
                        MButton {
                            kind: "tonal"
                            text: root.tr("action.shuffle")
                            enabled: root.nav.collectionCount > 0
                            onClicked: root.nav.playCollection(true)
                        }
                    }
                }
            }
        }

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
            onActivated: root.nav.playCollectionItem(index)
            onAddRequested: root.nav.addCollectionItem(index)
            onPlayNextRequested: root.nav.playNextCollectionItem(index)
        }
    }

    ColumnLayout {
        anchors.centerIn: parent
        width: parent.width - 48
        visible: list.count === 0 && root.nav.status !== ""
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
