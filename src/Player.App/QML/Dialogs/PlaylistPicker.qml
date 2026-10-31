import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic
import "../Components"

// "Add to a playlist": the user's playlists (Liked Music is left out) and a field to make a new one with the song in
// it. vm is the LibraryViewModel; show(videoId) opens it.
MDialog {
    id: dialog

    required property var vm
    property string videoId: ""

    title: dialog.tr("picker.title")

    function show(id) {
        videoId = id
        newName.text = ""
        vm.loadLibrary()
        open()
    }

    ListView {
        id: lists
        Layout.fillWidth: true
        Layout.preferredHeight: Math.min(Math.max(contentHeight, 64), 280)
        clip: true
        boundsBehavior: Flickable.StopAtBounds
        model: dialog.vm.library
        ScrollBar.vertical: ScrollBar {}

        delegate: TrackItem {
            required property var model
            readonly property bool addable: model.videoId !== "LM"

            visible: addable
            width: ListView.view.width
            height: addable ? implicitHeight : 0
            heading: model.title
            supporting: model.artist
            artUrl: model.artUrl
            onActivated: {
                dialog.vm.addToPlaylist(model.videoId, dialog.videoId)
                dialog.close()
            }
        }
    }

    RowLayout {
        Layout.fillWidth: true
        spacing: 8

        TextField {
            id: newName
            Layout.fillWidth: true
            implicitHeight: 40
            placeholderText: dialog.tr("picker.newName")
            placeholderTextColor: dialog.theme.inkSurfaceVariant
            color: dialog.theme.inkSurface
            font.pixelSize: 14
            selectByMouse: true
            leftPadding: 16
            rightPadding: 16
            verticalAlignment: TextInput.AlignVCenter
            background: Rectangle {
                radius: 8
                color: "transparent"
                border.width: newName.activeFocus ? 2 : 1
                border.color: newName.activeFocus ? dialog.theme.primary : dialog.theme.outline
            }
            onAccepted: if (create.enabled) create.clicked()
        }
        MButton {
            id: create
            kind: "tonal"
            text: dialog.tr("picker.create")
            enabled: newName.text.trim().length > 0
            onClicked: {
                dialog.vm.createPlaylist(newName.text, dialog.videoId)
                dialog.close()
            }
        }
    }

    RowLayout {
        Layout.fillWidth: true

        Item { Layout.fillWidth: true }

        MButton {
            kind: "text"
            text: dialog.tr("action.cancel")
            onClicked: dialog.close()
        }
    }
}
