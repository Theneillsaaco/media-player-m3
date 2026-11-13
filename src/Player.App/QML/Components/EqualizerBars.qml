import QtQuick

// Three bars that dance while a song plays and freeze when it is paused. Shown over the cover of the current row.
Row {
    id: root

    property bool running: false
    property color color: "#FFFFFF"

    spacing: 2
    height: 14

    Repeater {
        model: 3

        Rectangle {
            id: bar

            required property int index

            width: 3
            radius: 1.5
            height: 8
            y: root.height - height
            color: root.color

            SequentialAnimation on height {
                running: root.running && root.visible
                loops: Animation.Infinite
                NumberAnimation { to: 14; duration: 360 + bar.index * 110; easing.type: Easing.InOutSine }
                NumberAnimation { to: 4; duration: 360 + bar.index * 110; easing.type: Easing.InOutSine }
            }
        }
    }
}
