import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic

// "Reproduciendo": the cover of the playing track, which turns into its music video when there is one to show.
// vm is the VideoViewModel, playback the PlaybackViewModel.
Rectangle {
    id: root

    required property var vm
    required property var playback
    readonly property var theme: Window.window ? Window.window.theme : null
    readonly property bool videoReady: videoLoader.item !== null && videoLoader.item.ready

    color: theme ? theme.surfaceContainerLow : "#F7F2FA"

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: 16
        spacing: 16

        // The stage: square for a cover, 16:9 for a video.
        Rectangle {
            id: stage
            Layout.fillWidth: true
            Layout.preferredHeight: root.vm.showing ? width * 9 / 16 : width
            radius: 16
            clip: true
            color: root.theme ? root.theme.surfaceContainerHighest : "#E6E0E9"

            Behavior on Layout.preferredHeight { NumberAnimation { duration: 250; easing.type: Easing.OutCubic } }

            Icon {
                anchors.centerIn: parent
                name: "music"
                size: 64
                visible: cover.status !== Image.Ready
            }
            Image {
                id: cover
                anchors.fill: parent
                source: root.playback.currentArt
                asynchronous: true
                fillMode: Image.PreserveAspectCrop
                sourceSize.width: 544
                sourceSize.height: 544
                opacity: root.videoReady ? 0 : 1
                Behavior on opacity { NumberAnimation { duration: 300 } }
            }

            // QtMultimedia is only touched when a video is to be shown, and a failure to load it stays here.
            Loader {
                id: videoLoader
                anchors.fill: parent
                active: root.vm.showing && root.vm.url !== ""
                source: "VideoSurface.qml"
                opacity: root.videoReady ? 1 : 0
                Behavior on opacity { NumberAnimation { duration: 300 } }
                onLoaded: {
                    item.vm = root.vm
                    item.playback = root.playback
                }
            }
        }

        ColumnLayout {
            Layout.fillWidth: true
            spacing: 4

            Text {
                Layout.fillWidth: true
                text: root.playback.currentTitle
                color: root.theme ? root.theme.onSurface : "#1D1B20"
                font.pixelSize: 20
                font.weight: Font.Medium
                wrapMode: Text.Wrap
                maximumLineCount: 2
                elide: Text.ElideRight
            }
            Text {
                Layout.fillWidth: true
                text: root.playback.currentArtist
                color: root.theme ? root.theme.onSurfaceVariant : "#49454F"
                font.pixelSize: 14
                elide: Text.ElideRight
            }
        }

        RowLayout {
            Layout.fillWidth: true
            visible: root.vm.available
            spacing: 8

            MButton {
                kind: root.vm.showing ? "tonal" : "outlined"
                text: root.vm.showing ? "Ver carátula" : "Ver video"
                onClicked: root.vm.toggleVideo()
            }
            Text {
                Layout.fillWidth: true
                text: root.vm.message
                visible: text.length > 0
                color: root.theme ? root.theme.onSurfaceVariant : "#49454F"
                font.pixelSize: 12
                wrapMode: Text.Wrap
            }
            MButton {
                visible: root.vm.showing && !root.vm.loading && root.vm.url === "" && root.vm.message.length > 0
                kind: "text"
                text: "Reintentar"
                onClicked: root.vm.retry()
            }
        }

        Text {
            Layout.fillWidth: true
            visible: videoLoader.status === Loader.Error
            text: "El video necesita QtMultimedia, que esta instalación de Qt no tiene."
            color: root.theme ? root.theme.error : "#BA1A1A"
            font.pixelSize: 12
            wrapMode: Text.Wrap
        }

        Item { Layout.fillHeight: true }
    }
}
