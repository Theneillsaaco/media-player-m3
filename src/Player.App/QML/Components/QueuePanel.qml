import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic

// The play queue. vm is the QueueViewModel.
Rectangle {
    id: root

    required property var vm
    readonly property var theme: Window.window ? Window.window.theme : null

    color: theme ? theme.surfaceContainerLow : "#F7F2FA"

    ColumnLayout {
        anchors.fill: parent
        spacing: 0
        Text {
            Layout.margins: 16
            text: "Cola" + (root.vm.count > 0 ? " · " + root.vm.count : "")
            color: root.theme ? root.theme.onSurface : "#1D1B20"
            font.pixelSize: 16
            font.weight: Font.Medium
        }
        ListView {
            id: queueView
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
                current: index === root.vm.position
                onActivated: root.vm.playItem(index)
            }
            Text {
                anchors.centerIn: parent
                visible: queueView.count === 0
                text: "La cola está vacía"
                color: root.theme ? root.theme.onSurfaceVariant : "#49454F"
                font.pixelSize: 14
            }
        }
    }
}
