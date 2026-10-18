import QtQuick
import QtQuick.Controls.Basic

// Material 3 menu: surface container, 12dp corners. Put MMenuItem children inside it.
// Declare it inside the item it belongs to (the theme is found through that item's window).
Menu {
    id: menu

    readonly property var theme: parent && parent.Window.window ? parent.Window.window.theme : null

    padding: 8
    modal: false
    focus: true
    closePolicy: Popup.CloseOnEscape | Popup.CloseOnPressOutside

    enter: Transition { NumberAnimation { property: "opacity"; from: 0; to: 1; duration: 100 } }
    exit: Transition { NumberAnimation { property: "opacity"; from: 1; to: 0; duration: 80 } }

    background: Rectangle {
        implicitWidth: 248
        radius: 12
        color: menu.theme ? menu.theme.surfaceContainer : "#F2ECF4"
        border.width: 1
        border.color: menu.theme ? menu.theme.outlineVariant : "#CAC4CF"
    }
}
