import QtQuick
import QtQuick.Controls.Basic

// Material 3 common button (40dp tall, full shape, label-large 14sp / weight 500).
// State layers: hover 8%, focus 10%, pressed 10% of the content color.
// Expressive motion: a pressed button squares off and shrinks a little along a spring, then settles back.
// Imports the Basic style explicitly so no system style (Breeze, Oxygen...) can change it.
// kind: "filled" | "tonal" | "outlined" | "text"
Button {
    id: control

    readonly property var theme: Window.window ? Window.window.theme : null

    property string kind: "filled"
    property color cPrimary: theme ? theme.primary : "#6750A4"
    property color cOnPrimary: theme ? theme.inkPrimary : "#FFFFFF"
    property color cSecondaryContainer: theme ? theme.secondaryContainer : "#E8DEF8"
    property color cOnSecondaryContainer: theme ? theme.inkSecondaryContainer : "#1D192B"
    property color cOutline: theme ? theme.outline : "#79747E"
    property color cOnSurface: theme ? theme.inkSurface : "#1D1B20"

    readonly property color content: kind === "filled" ? cOnPrimary
                                   : kind === "tonal" ? cOnSecondaryContainer : cPrimary
    readonly property color container: kind === "filled" ? cPrimary
                                     : kind === "tonal" ? cSecondaryContainer : "transparent"
    readonly property bool hasContainer: kind === "filled" || kind === "tonal"

    // The corner radius follows a spring: pill at rest, squarer while pressed.
    property real shapeRadius: down ? 12 : height / 2
    Behavior on shapeRadius { Anim { type: Anim.FastSpatial } }

    scale: down ? 0.97 : 1
    Behavior on scale { Anim { type: Anim.FastSpatial } }

    implicitHeight: 40
    implicitWidth: Math.max(64, contentItem.implicitWidth + leftPadding + rightPadding)
    leftPadding: kind === "text" ? 12 : 24
    rightPadding: leftPadding
    hoverEnabled: true
    focusPolicy: Qt.StrongFocus

    contentItem: Text {
        text: control.text
        font.pixelSize: 14
        font.weight: Font.Medium
        font.letterSpacing: 0.1
        color: control.enabled ? control.content : Qt.alpha(control.cOnSurface, 0.38)
        horizontalAlignment: Text.AlignHCenter
        verticalAlignment: Text.AlignVCenter
        elide: Text.ElideRight
    }

    background: Rectangle {
        radius: Math.max(0, control.shapeRadius)
        color: !control.hasContainer ? "transparent"
             : control.enabled ? control.container : Qt.alpha(control.cOnSurface, 0.12)
        border.width: control.kind === "outlined" ? 1 : 0
        border.color: control.enabled ? control.cOutline : Qt.alpha(control.cOnSurface, 0.12)

        Behavior on color { ColorAnimation { duration: 150 } }

        Rectangle { // state layer
            anchors.fill: parent
            radius: parent.radius
            color: control.content
            opacity: !control.enabled ? 0
                   : control.down ? 0.10
                   : control.visualFocus ? 0.10
                   : control.hovered ? 0.08 : 0
            Behavior on opacity { NumberAnimation { duration: 100 } }
        }
        Rectangle { // focus indicator: 3dp ring, 2dp outside the container
            anchors.fill: parent
            anchors.margins: -5
            radius: parent.radius + 5
            color: "transparent"
            border.width: 3
            border.color: control.cPrimary
            visible: control.visualFocus
        }
    }
}
