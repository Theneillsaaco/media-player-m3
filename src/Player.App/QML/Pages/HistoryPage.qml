import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic
import "../Components"

// What was listened to, newest first (the songs that counted as a listen). vm is the HistoryViewModel.
Item {
    id: root

    required property var vm
    readonly property var theme: Window.window ? Window.window.theme : null
    readonly property var i18n: Window.window ? Window.window.i18n : null

    function tr(key) { return i18n ? i18n.t(key) : key }

    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        RowLayout {
            Layout.fillWidth: true
            Layout.margins: 20
            Layout.bottomMargin: 8
            Layout.rightMargin: 12
            spacing: 8

            Text {
                Layout.fillWidth: true
                text: root.tr("history.title") + (root.vm.count > 0 ? " · " + root.vm.count : "")
                color: root.theme ? root.theme.inkSurface : "#1D1B20"
                font.pixelSize: 20
                font.weight: Font.Medium
                elide: Text.ElideRight
            }
            MButton {
                kind: "text"
                visible: root.vm.count > 0
                text: root.tr("history.clear")
                onClicked: {
                    root.vm.clearAll()
                    if (Window.window && Window.window.toast)
                        Window.window.toast(root.tr("history.cleared"))
                }
            }
        }

        ListView {
            id: view
            Layout.fillWidth: true
            Layout.fillHeight: true
            clip: true
            boundsBehavior: Flickable.StopAtBounds
            model: root.vm.items
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
                onActivated: root.vm.playItem(index)
                onAddRequested: root.vm.addItem(index)
                onPlayNextRequested: root.vm.playNextItem(index)
            }

            Text {
                anchors.centerIn: parent
                width: parent.width - 48
                horizontalAlignment: Text.AlignHCenter
                wrapMode: Text.Wrap
                visible: view.count === 0
                text: root.tr("history.empty")
                color: root.theme ? root.theme.inkSurfaceVariant : "#49454F"
                font.pixelSize: 14
            }
        }
    }
}
