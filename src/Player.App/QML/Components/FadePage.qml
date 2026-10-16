import QtQuick

// Holds one page and shows it with Material's "fade through": the page that leaves fades out quickly (90 ms), the
// one that arrives waits for that and fades in (210 ms). Put the page inside it with anchors.fill: parent.
Item {
    id: root

    property bool shown: false
    default property alias content: holder.data

    anchors.fill: parent
    opacity: shown ? 1 : 0
    visible: opacity > 0

    Behavior on opacity {
        SequentialAnimation {
            PauseAnimation { duration: root.shown ? 90 : 0 }
            NumberAnimation {
                duration: root.shown ? 210 : 90
                easing.type: Easing.OutCubic
            }
        }
    }

    Item {
        id: holder
        anchors.fill: parent
    }
}
