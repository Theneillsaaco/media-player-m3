import QtQuick
import QtQuick.Shapes

// Cover art with rounded corners and a placeholder while it loads. The picture is the fill of a rounded shape:
// real clipping, so it does not depend on the color behind it and costs no extra texture.
// The picture fades in when it is ready, so a new cover does not pop.
Item {
    id: root

    readonly property var theme: Window.window ? Window.window.theme : null

    property string source: ""
    property real radius: 8
    property int pixels: 96
    property string icon: "music"
    property color backdrop: "transparent" // kept so existing callers still compile; no longer used

    readonly property real r: Math.max(0, Math.min(radius, width / 2, height / 2))

    implicitWidth: 48
    implicitHeight: 48

    Rectangle {
        anchors.fill: parent
        radius: root.r
        color: root.theme ? root.theme.surfaceContainerHighest : "#E6E0E9"
    }

    Icon {
        anchors.centerIn: parent
        name: root.icon
        size: Math.max(16, Math.min(40, root.width * 0.4))
        tint: root.theme ? root.theme.inkSurfaceVariant : "#49454F"
        visible: img.status !== Image.Ready
    }

    // The cropped picture is rendered once into this layer, and the layer is what fills the shape.
    Item {
        id: cropped
        width: root.width
        height: root.height
        visible: false
        layer.enabled: true
        layer.smooth: true

        Image {
            id: img
            anchors.fill: parent
            source: root.source
            asynchronous: true
            fillMode: Image.PreserveAspectCrop
            sourceSize.width: root.pixels
            sourceSize.height: root.pixels
        }
    }

    Shape {
        anchors.fill: parent
        opacity: img.status === Image.Ready ? 1 : 0
        visible: opacity > 0
        preferredRendererType: Shape.CurveRenderer

        Behavior on opacity { Anim { type: Anim.DefaultEffects } }

        ShapePath {
            strokeColor: "transparent"
            fillItem: cropped
            PathSvg {
                path: "M" + root.r + ",0 H" + (root.width - root.r)
                    + " A" + root.r + "," + root.r + " 0 0 1 " + root.width + "," + root.r
                    + " V" + (root.height - root.r)
                    + " A" + root.r + "," + root.r + " 0 0 1 " + (root.width - root.r) + "," + root.height
                    + " H" + root.r
                    + " A" + root.r + "," + root.r + " 0 0 1 0," + (root.height - root.r)
                    + " V" + root.r
                    + " A" + root.r + "," + root.r + " 0 0 1 " + root.r + ",0 Z"
            }
        }
    }
}
