import QtQuick
import QtQuick.Controls.Basic
import QtQuick.Shapes

// Material 3 slider. Two looks:
//   expressive: false  a flat track (volume).
//   expressive: true   the progress is a wave that moves while `playing` and goes flat when it is not (the seek bar).
// Both have an active and an inactive track with a gap around the handle, a 4 dp handle that narrows only while it
// is pressed, and a stop dot at the end.
Slider {
    id: slider

    readonly property var theme: Window.window ? Window.window.theme : null

    property bool expressive: false
    property bool playing: false
    property string valueLabel: ""

    property color cPrimary: theme ? theme.primary : "#6750A4"
    property color cInactive: theme ? theme.secondaryContainer : "#E8DEF8"
    property color cOnSurface: theme ? theme.inkSurface : "#1D1B20"
    property color cInverse: theme ? theme.inverseSurface : "#313033"
    property color cOnInverse: theme ? theme.inverseOnSurface : "#F4EFF4"

    signal changedByUser(real v)

    readonly property real trackHeight: 7
    readonly property real handleWidth: 4
    readonly property real handleHeight: 24
    readonly property real gap: 8
    readonly property real stopSize: 3
    readonly property real amplitude: 2.5
    readonly property real wavelength: 48
    readonly property real waveBox: 14

    padding: 0
    implicitHeight: 28
    implicitWidth: 160
    hoverEnabled: true
    opacity: enabled ? 1 : 0.38

    readonly property real thumbCenter: visualPosition * (availableWidth - handleWidth) + handleWidth / 2
    readonly property real activeEnd: Math.max(0, thumbCenter - handleWidth / 2 - gap)
    readonly property real inactiveStart: Math.min(availableWidth, thumbCenter + handleWidth / 2 + gap)

    // ---- the wave ----

    property real amp: playing ? amplitude : 0
    Behavior on amp { NumberAnimation { duration: 180; easing.type: Easing.OutCubic } }

    property real shift: 0 // how far the wave has slid, 0 .. wavelength

    NumberAnimation on shift {
        from: 0
        to: slider.wavelength
        duration: 1400
        loops: Animation.Infinite
        running: slider.expressive && slider.playing && slider.visible
    }

    function buildWave(a, width, wl, box, sh) {
        if (width <= 0) return []
        const points = []
        
        for (let x = 0; x <= width; x += 2) {
            points.push(Qt.point(x, box / 2 + a * Math.sin(2 * Math.PI * (x + sh) / wl)))
        }
        
        if (points.length === 0 || points[points.length - 1].x !== width) {
            points.push(Qt.point(width, box / 2 + a * Math.sin(2 * Math.PI * (width + sh) / wl)))
        }
        
        return points
    }

    readonly property var wavePath: expressive ? buildWave(amp, activeEnd, wavelength, waveBox, shift) : []

    onMoved: {
        if (slider.live) slider.changedByUser(slider.value)
    }
    onPressedChanged: {
        if (!slider.pressed && !slider.live) slider.changedByUser(slider.value)
    }

    background: Item {
        x: slider.leftPadding
        y: slider.topPadding + (slider.availableHeight - height) / 2
        width: slider.availableWidth
        height: slider.trackHeight

        // active track, flat (expressive: false)
        Rectangle {
            visible: !slider.expressive && slider.activeEnd > 0.5
            width: slider.activeEnd
            height: parent.height
            radius: height / 2
            topRightRadius: 2
            bottomRightRadius: 2
            color: slider.cPrimary
        }

        // active track, wave (expressive: true)
        Item {
            visible: slider.expressive && slider.activeEnd > 0.5
            y: (parent.height - slider.waveBox) / 2
            width: slider.activeEnd
            height: slider.waveBox

            Shape {
                width: slider.activeEnd
                height: slider.waveBox
                preferredRendererType: Shape.CurveRenderer

                ShapePath {
                    strokeColor: slider.cPrimary
                    strokeWidth: slider.trackHeight
                    fillColor: "transparent"
                    capStyle: ShapePath.RoundCap
                    joinStyle: ShapePath.RoundJoin
                    PathPolyline { path: slider.wavePath }
                }
            }
        }

        // inactive track
        Rectangle {
            x: slider.inactiveStart
            width: Math.max(0, parent.width - x)
            height: parent.height
            visible: width > 0.5
            radius: height / 2
            topLeftRadius: 2
            bottomLeftRadius: 2
            color: slider.cInactive
        }

        // stop dot
        Rectangle {
            x: parent.width - width - (parent.height - height) / 2
            anchors.verticalCenter: parent.verticalCenter
            width: slider.stopSize
            height: slider.stopSize
            radius: height / 2
            color: slider.cPrimary
            visible: slider.inactiveStart < parent.width - 16
        }
    }

    handle: Rectangle {
        property real w: slider.pressed ? 2 : slider.handleWidth

        x: slider.leftPadding + slider.visualPosition * (slider.availableWidth - slider.handleWidth) + (slider.handleWidth - w) / 2
        y: slider.topPadding + (slider.availableHeight - height) / 2
        width: w
        height: slider.handleHeight
        radius: width / 2
        color: slider.cPrimary

        Behavior on w { NumberAnimation { duration: 120; easing.type: Easing.OutCubic } }
    }

    ToolTip {
        visible: slider.valueLabel.length > 0 && slider.pressed
        delay: 0
        timeout: -1
        x: slider.leftPadding + slider.visualPosition * (slider.availableWidth - width)
        y: -height - 12
        padding: 8
        
        contentItem: Text {
            text: slider.valueLabel
            color: slider.cOnInverse
            font.pixelSize: 12
        }
        
        background: Rectangle {
            color: slider.cInverse
            radius: height / 2
        }
    }
}