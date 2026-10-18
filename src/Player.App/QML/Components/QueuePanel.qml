import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic

// The play queue. vm is the QueueViewModel. Main.qml gives it rounded corners (it is a card next to the pages).
// Drag the grip of a row to reorder: a line shows where it lands; near the top or bottom edge the list scrolls.
Rectangle {
    id: root

    required property var vm
    readonly property var theme: Window.window ? Window.window.theme : null
    readonly property var i18n: Window.window ? Window.window.i18n : null

    // Drag state: the position being dragged, where it would land, and the pointer in window coordinates.
    property int dragFrom: -1
    property int dragTo: -1
    property real dragSceneY: 0
    property real dragLocalY: 0
    property string dragTitle: ""
    // After a move or a removal the model is rebuilt: keep the scroll where it was.
    property bool holdScroll: false
    readonly property bool dragging: dragFrom >= 0

    color: theme ? theme.surfaceContainerLow : "#F7F2FA"

    function tr(key) { return i18n ? i18n.t(key) : key }

    function retarget() {
        const y = queueView.mapFromItem(null, 0, dragSceneY).y
        dragLocalY = y
        const index = queueView.indexAt(queueView.width / 2, queueView.contentY + Math.max(0, Math.min(queueView.height - 1, y)))
        dragTo = index >= 0 ? index : Math.max(0, queueView.count - 1)
    }

    function keepScroll(change) {
        const y = queueView.contentY
        holdScroll = true
        change()
        Qt.callLater(() => {
            queueView.contentY = y
            holdScroll = false
        })
    }

    function finishDrag(drop) {
        const from = dragFrom
        const to = dragTo
        dragFrom = -1
        dragTo = -1
        if (drop && from >= 0 && to >= 0 && from !== to)
            keepScroll(() => root.vm.moveItem(from, to))
    }

    // The playing track changed: bring it into view (not while dragging or right after an edit).
    Connections {
        target: root.vm
        function onPositionChanged() {
            if (!root.dragging && !root.holdScroll && root.vm.position >= 0)
                queueView.positionViewAtIndex(root.vm.position, ListView.Contain)
        }
    }

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
                text: root.tr("queue.title") + (root.vm.count > 0 ? " · " + root.vm.count : "")
                color: root.theme ? root.theme.inkSurface : "#1D1B20"
                font.pixelSize: 18
                font.weight: Font.Medium
                elide: Text.ElideRight
            }
            MButton {
                kind: "text"
                visible: root.vm.count > 0
                text: root.tr("queue.clear")
                onClicked: {
                    root.vm.clearAll()
                    if (Window.window && Window.window.toast)
                        Window.window.toast(root.tr("queue.cleared"))
                }
            }
        }

        Item {
            Layout.fillWidth: true
            Layout.fillHeight: true

            ListView {
                id: queueView
                anchors.fill: parent
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
                    current: index === root.vm.position
                    draggable: true
                    showRemove: true
                    dimmed: root.dragFrom === index
                    onActivated: root.vm.playItem(index)
                    onRemoveRequested: {
                        root.keepScroll(() => root.vm.removeItem(index))
                        if (Window.window && Window.window.toast)
                            Window.window.toast(root.tr("queue.removed"))
                    }
                    onGripPressed: {
                        root.dragFrom = index
                        root.dragTo = index
                        root.dragTitle = model.title
                    }
                    onGripMoved: (sceneX, sceneY) => {
                        root.dragSceneY = sceneY
                        root.retarget()
                    }
                    onGripReleased: (dropped) => root.finishDrag(dropped)
                }

                Text {
                    anchors.centerIn: parent
                    visible: queueView.count === 0
                    text: root.tr("queue.empty")
                    color: root.theme ? root.theme.inkSurfaceVariant : "#49454F"
                    font.pixelSize: 14
                }
            }

            // Where the dragged entry would land.
            Rectangle {
                visible: root.dragging && root.dragTo !== root.dragFrom
                x: 12
                width: parent.width - 24
                height: 3
                radius: 1.5
                z: 5
                color: root.theme ? root.theme.primary : "#6750A4"
                y: {
                    const item = queueView.itemAtIndex(root.dragTo)
                    if (!item) return -10
                    return (root.dragTo > root.dragFrom ? item.y + item.height : item.y) - queueView.contentY - 1
                }
            }

            // The entry under the pointer.
            Rectangle {
                visible: root.dragging
                x: 8
                width: parent.width - 16
                height: 48
                radius: 12
                z: 10
                opacity: 0.92
                color: root.theme ? root.theme.secondaryContainer : "#E8DEF8"
                y: Math.max(0, Math.min(parent.height - height, root.dragLocalY - height / 2))

                Text {
                    anchors.fill: parent
                    anchors.leftMargin: 16
                    anchors.rightMargin: 16
                    verticalAlignment: Text.AlignVCenter
                    text: root.dragTitle
                    color: root.theme ? root.theme.inkSecondaryContainer : "#1D192B"
                    font.pixelSize: 14
                    font.weight: Font.Medium
                    elide: Text.ElideRight
                }
            }

            // Scrolls while the pointer is near the top or the bottom edge.
            Timer {
                interval: 16
                repeat: true
                running: root.dragging
                onTriggered: {
                    const y = root.dragLocalY
                    const edge = 48
                    let dy = 0
                    if (y < edge) dy = -Math.ceil((edge - y) / 6)
                    else if (y > queueView.height - edge) dy = Math.ceil((y - (queueView.height - edge)) / 6)
                    if (dy === 0) return
                    const low = queueView.originY
                    const high = Math.max(low, queueView.originY + queueView.contentHeight - queueView.height)
                    queueView.contentY = Math.max(low, Math.min(high, queueView.contentY + dy))
                    root.retarget()
                }
            }
        }
    }
}
