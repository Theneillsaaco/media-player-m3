import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic

// The lyrics of the playing track. vm is the LyricsViewModel. Timed lyrics follow the music: the current line
// is centred, bold and full size, the others shrink and fade with their distance from it, and a click on a line
// jumps there. Plain lyrics just scroll. The top and bottom edges fade out.
Rectangle {
    id: root

    required property var vm
    readonly property var theme: Window.window ? Window.window.theme : null
    readonly property var i18n: Window.window ? Window.window.i18n : null
    readonly property bool hasLines: vm.state === "synced" || vm.state === "plain"

    color: theme ? theme.surfaceContainerLow : "#F7F2FA"

    function tr(key) { return i18n ? i18n.t(key) : key }

    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        Text {
            Layout.fillWidth: true
            Layout.margins: 16
            Layout.bottomMargin: 4
            text: root.tr("lyrics.title") + (root.vm.source.length > 0 ? " · " + root.vm.source : "")
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

            MButton { kind: "text"; text: "A−"; ToolTip.visible: hovered; ToolTip.text: root.tr("tip.smaller"); onClicked: root.vm.shrinkText() }
            MButton { kind: "text"; text: "A+"; ToolTip.visible: hovered; ToolTip.text: root.tr("tip.bigger"); onClicked: root.vm.growText() }
            MButton { kind: "text"; text: root.tr("lyrics.extra." + root.vm.extraMode); ToolTip.visible: hovered; ToolTip.text: root.tr("tip.lyricsExtra"); onClicked: root.vm.cycleExtra() }
            Item { Layout.fillWidth: true }
            MButton { visible: root.vm.synced; kind: "text"; text: "−0,5"; ToolTip.visible: hovered; ToolTip.text: root.tr("tip.sooner"); onClicked: root.vm.nudgeOffset(-0.5) }
            MButton {
                visible: root.vm.synced
                kind: "text"
                text: root.vm.offsetText
                ToolTip.visible: hovered
                ToolTip.text: root.tr("tip.resetDelay")
                onClicked: root.vm.clearOffset()
            }
            MButton { visible: root.vm.synced; kind: "text"; text: "+0,5"; ToolTip.visible: hovered; ToolTip.text: root.tr("tip.later"); onClicked: root.vm.nudgeOffset(0.5) }
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
                ScrollBar.vertical: MScrollBar {}

                // Countdown during a long intro: three dots that fill in one by one
                header: Item {
                    width: list.width
                    height: root.vm.countdown > 0 ? 48 : 0
                    visible: height > 0

                    Row {
                        anchors.centerIn: parent
                        spacing: 10

                        Repeater {
                            model: 3

                            Rectangle {
                                required property int index
                                // 3 s left: no dot lit yet; the dots light up as the time runs out
                                readonly property bool lit: root.vm.countdown <= 3 - index
                                width: 10
                                height: 10
                                radius: 5
                                color: root.theme ? root.theme.primary : "#6750A4"
                                opacity: lit ? 1 : 0.3
                                scale: lit ? 1.15 : 0.85
                                Behavior on opacity { Anim { type: Anim.FastEffects } }
                                Behavior on scale { Anim { type: Anim.FastSpatial } }
                            }
                        }

                        Text {
                            anchors.verticalCenter: parent.verticalCenter
                            text: root.vm.countdown + " s"
                            color: root.theme ? root.theme.primary : "#6750A4"
                            font.pixelSize: 14
                        }
                    }
                }

                delegate: Item {
                    id: row
                    required property int index
                    required property var model
                    readonly property bool current: root.vm.synced && index === root.vm.currentIndex
                    readonly property int distance: Math.abs(index - root.vm.currentIndex)

                    width: ListView.view.width
                    height: textColumn.implicitHeight + 14

                    Column {
                        id: textColumn
                        x: 20
                        width: parent.width - 40
                        anchors.verticalCenter: parent.verticalCenter
                        spacing: 2
                        transformOrigin: Item.Left
                        // The line being sung is full size and opaque; the others recede with their distance.
                        scale: row.current || !root.vm.synced ? 1 : 0.94
                        opacity: row.current || !root.vm.synced ? 1 : Math.max(0.5, 0.74 - 0.08 * row.distance)
                        Behavior on scale { Anim { type: Anim.DefaultSpatial } }
                        Behavior on opacity { Anim { type: Anim.DefaultEffects } }

                        Text {
                            width: parent.width
                            height: Math.max(implicitHeight, root.vm.fontSize)
                            text: row.model.text
                            wrapMode: Text.Wrap
                            font.pixelSize: root.vm.fontSize
                            font.weight: row.current ? Font.DemiBold : Font.Normal
                            color: root.theme ? root.theme.inkSurface : "#1D1B20"
                            Behavior on color { ColorAnimation { duration: 200 } }
                        }
                        Text {
                            visible: text.length > 0
                            width: parent.width
                            text: row.model.sub
                            wrapMode: Text.Wrap
                            font.pixelSize: Math.round(root.vm.fontSize * 0.75)
                            color: root.theme ? root.theme.primary : "#6750A4"
                        }
                    }
                    MouseArea {
                        anchors.fill: parent
                        enabled: root.vm.synced
                        onClicked: root.vm.seekToLine(row.index)
                    }
                }
            }

            // Edges that fade out, so the lines come and go instead of being cut.
            Rectangle {
                visible: root.hasLines
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.top: parent.top
                height: 28
                gradient: Gradient {
                    GradientStop { position: 0.0; color: root.color }
                    GradientStop { position: 1.0; color: Qt.alpha(root.color, 0) }
                }
            }
            Rectangle {
                visible: root.hasLines
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.bottom: parent.bottom
                height: 48
                gradient: Gradient {
                    GradientStop { position: 0.0; color: Qt.alpha(root.color, 0) }
                    GradientStop { position: 1.0; color: root.color }
                }
            }

            // No lines to show: loading, nothing found, instrumental, error...
            ColumnLayout {
                anchors.centerIn: parent
                width: parent.width - 48
                visible: !root.hasLines
                spacing: 16

                LoadingIndicator {
                    Layout.alignment: Qt.AlignHCenter
                    visible: root.vm.loading
                }

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
                    text: root.tr("action.retry")
                    onClicked: root.vm.retry()
                }
            }
        }
    }

    // The rounded corners of the card, over the lines.
    CornerMask {
        anchors.fill: parent
        z: 20
        radius: root.radius
        color: root.theme ? root.theme.surface : "#FDF7FF"
    }
}
