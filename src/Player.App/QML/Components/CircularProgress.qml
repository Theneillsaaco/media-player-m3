import QtQuick
import QtQuick.Shapes

// Material 3 circular progress, indeterminate (the YouTube kind): an arc that turns all the time while it grows and
// shrinks, the head running ahead of the tail. Two animated numbers and one arc; it stops by itself when hidden.
Item {
    id: root

    readonly property var theme: Window.window ? Window.window.theme : null

    property bool running: true
    property real size: 48
    property real thickness: Math.max(2, Math.round(size / 12))
    property color color: theme ? theme.primary : "#65558f"

    implicitWidth: size
    implicitHeight: size

    property real spin: 0 // the constant turn, degrees
    property real t: 0    // 0 .. 4: four grow-and-shrink cycles, so the 270 degrees each one advances add up to whole turns

    readonly property int cycle: Math.floor(t)
    readonly property real p: t - cycle

    function ease(x) { return x < 0.5 ? 4 * x * x * x : 1 - Math.pow(-2 * x + 2, 3) / 2 }

    readonly property real head: 20 + 270 * ease(Math.min(1, p / 0.6))
    readonly property real tail: 270 * ease(Math.max(0, (p - 0.4) / 0.6))

    NumberAnimation on spin {
        from: 0
        to: 360
        duration: 1568
        loops: Animation.Infinite
        running: root.running && root.visible
    }

    NumberAnimation on t {
        from: 0
        to: 4
        duration: 4 * 1333
        loops: Animation.Infinite
        running: root.running && root.visible
    }

    Shape {
        anchors.fill: parent
        rotation: root.spin
        preferredRendererType: Shape.CurveRenderer

        ShapePath {
            strokeColor: root.color
            strokeWidth: root.thickness
            fillColor: "transparent"
            capStyle: ShapePath.RoundCap

            PathAngleArc {
                centerX: root.size / 2
                centerY: root.size / 2
                radiusX: (root.size - root.thickness) / 2
                radiusY: (root.size - root.thickness) / 2
                startAngle: root.cycle * 270 + root.tail - 90
                sweepAngle: root.head - root.tail
            }
        }
    }
}
