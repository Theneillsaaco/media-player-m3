import QtQuick
import QtQuick.Controls.Basic
import "../Components"

// Search results. vm is the SearchViewModel; the status line shows while the list is empty.
Item {
    id: root

    required property var vm

    ListView {
        id: resultsView
        anchors.fill: parent
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
            showAdd: true
            onActivated: root.vm.playResult(index)
            onAddRequested: root.vm.addResult(index)
        }
    }
    Text {
        anchors.centerIn: parent
        width: parent.width - 48
        horizontalAlignment: Text.AlignHCenter
        wrapMode: Text.Wrap
        visible: resultsView.count === 0
        text: root.vm.status
        color: "#49454F"
        font.pixelSize: 14
    }
}
