import QtQuick
import QtQuick.Controls.Basic

// Thin Material scroll bar: a rounded handle that only shows while the list is scrolled, hovered or dragged and grows
// a little under the pointer. It keeps clear of the rounded corners of its card (padding top and bottom) and of the
// right edge, so it never lands on the buttons of the rows.
// Use it as: ScrollBar.vertical: MScrollBar {}
ScrollBar {
    id: bar

    readonly property var theme: Window.window ? Window.window.theme : null

    policy: ScrollBar.AsNeeded
    minimumSize: 0.1
    topPadding: 14
    bottomPadding: 14
    leftPadding: 3
    rightPadding: 3

    contentItem: Rectangle {
        implicitWidth: bar.hovered || bar.pressed ? 8 : 4
        implicitHeight: 32
        radius: width / 2
        color: bar.theme ? bar.theme.outline : "#79747E"
        opacity: bar.pressed ? 0.8 : bar.active ? 0.55 : 0

        Behavior on implicitWidth { NumberAnimation { duration: 120; easing.type: Easing.OutCubic } }
        Behavior on opacity { NumberAnimation { duration: 200 } }
    }
}
