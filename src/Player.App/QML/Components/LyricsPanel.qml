import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic

// The lyrics of the playing track. vm is the LyricsViewModel. Timed lyrics follow the music: the current line
// is centred and bold and a click on a line jumps there. Plain lyrics just scroll.
Rectangle {
    id: root

    required property var vm
    readonly property var theme: Window.window ? Window.window.theme : null
    readonly property bool hasLines: vm.state === "synced" || vm.state === "plain"

    color: theme ? theme.surfaceContainerLow : "#F7F2FA"

    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        Text {
            Layout.fillWidth: true
            Layout.margins: 16
            Layout.bottomMargin: 4
            text: "Letra" + (root.vm.source.length > 0 ? " · " + root.vm.source : "")
            color: root.theme ? root.theme.inkSurface : "#1D1B20"
            font.pixelSize: 16
            font.weight: Font.Medium
            elide: Text.ElideRight
        }

        // Text size, and for timed lyrics the delay (+ shows the lines later than the music)
        RowLayout {
            Layout.fillWidth: true
            Layout.leftMargin: 8
            Layout.rightMargin: 8
            visible: root.hasLines
            spacing: 0

            MButton { kind: "text"; text: "A−"; onClicked: root.vm.shrinkText() }
            MButton { kind: "text"; text: "A+"; onClicked: root.vm.growText() }
            Item { Layout.fillWidth: true }
            MButton { visible: root.vm.synced; kind: "text"; text: "−0,5"; onClicked: root.vm.nudgeOffset(-0.5) }
            MButton {
                visible: root.vm.synced
                kind: "text"
                text: root.vm.offsetText
                onClicked: root.vm.clearOffset()
            }
            MButton { visible: root.vm.synced; kind: "text"; text: "+0,5"; onClicked: root.vm.nudgeOffset(0.5) }
        }

        Item {
            Layout.fillWidth: true
            Layout.fillHeight: true

            ListView {
                id: list
                anchors.fill: parent
                visible: root.hasLines
                clip: true
                boundsBehavior: Flickable.StopAtBounds
                model: root.vm.lines
                currentIndex: root.vm.currentIndex

                // Keep the current line near the middle; the range is only applied when the line changes,
                // so the user can still scroll away and read ahead.
                highlightRangeMode: ListView.ApplyRange
                preferredHighlightBegin: height * 0.38
                preferredHighlightEnd: height * 0.62
                highlightMoveDuration: 350
                highlightFollowsCurrentItem: true

                topMargin: 16
                bottomMargin: height * 0.4
                ScrollBar.vertical: ScrollBar {}

                // Countdown during a long intro
                header: Item {
                    width: list.width
                    height: root.vm.countdown > 0 ? 48 : 0
                    visible: height > 0
                    Text {
                        anchors.centerIn: parent
                        text: "● ● ●   " + root.vm.countdown + " s"
                        color: root.theme ? root.theme.primary : "#6750A4"
                        font.pixelSize: 14
                    }
                }

                delegate: Item {
                    id: row
                    required property int index
                    required property var model
                    readonly property bool current: root.vm.synced && index === root.vm.currentIndex

                    width: ListView.view.width
                    height: Math.max(label.implicitHeight, root.vm.fontSize) + 14

                    Text {
                        id: label
                        x: 20
                        width: parent.width - 40
                        anchors.verticalCenter: parent.verticalCenter
                        text: row.model.text
                        wrapMode: Text.Wrap
                        font.pixelSize: root.vm.fontSize
                        font.weight: row.current ? Font.DemiBold : Font.Normal
                        color: root.theme ? (row.current || !root.vm.synced ? root.theme.inkSurface : root.theme.inkSurfaceVariant) : "#1D1B20"
                        opacity: row.current || !root.vm.synced ? 1 : 0.6
                        Behavior on opacity { NumberAnimation { duration: 200 } }
                    }
                    MouseArea {
                        anchors.fill: parent
                        enabled: root.vm.synced
                        onClicked: root.vm.seekToLine(row.index)
                    }
                }
            }

            // No lines to show: loading, nothing found, instrumental, error...
            ColumnLayout {
                anchors.centerIn: parent
                width: parent.width - 48
                visible: !root.hasLines
                spacing: 16

                Text {
                    Layout.fillWidth: true
                    horizontalAlignment: Text.AlignHCenter
                    wrapMode: Text.Wrap
                    text: root.vm.message
                    color: root.theme ? root.theme.inkSurfaceVariant : "#49454F"
                    font.pixelSize: 14
                }
                MButton {
                    Layout.alignment: Qt.AlignHCenter
                    visible: root.vm.state === "error"
                    kind: "tonal"
                    text: "Reintentar"
                    onClicked: root.vm.retry()
                }
            }
        }
    }
}
