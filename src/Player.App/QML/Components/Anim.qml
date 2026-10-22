import QtQuick

// Material 3 motion as one animation type, after the Anim of Caelestia but without its Tokens singleton: the
// numbers are the published M3 tokens. Spatial types move things (they overshoot a little, like a spring), effects
// types fade and recolor, Standard and Emphasized are the classic M3 curves.
//   Anim { target: item; property: "x"; to: 100; type: Anim.DefaultSpatial }
NumberAnimation {
    enum Type {
        Standard,
        Emphasized,
        FastSpatial,
        DefaultSpatial,
        SlowSpatial,
        FastEffects,
        DefaultEffects,
        SlowEffects
    }

    property int type: Anim.DefaultSpatial

    // duration in ms and the bezier control points of each type
    readonly property var specs: [
        { d: 300, c: [0.2, 0.0, 0.0, 1.0] },
        { d: 500, c: [0.05, 0.7, 0.1, 1.0] },
        { d: 350, c: [0.42, 1.67, 0.21, 0.90] },
        { d: 500, c: [0.38, 1.21, 0.22, 1.00] },
        { d: 650, c: [0.39, 1.29, 0.35, 0.98] },
        { d: 150, c: [0.31, 0.94, 0.34, 1.00] },
        { d: 200, c: [0.34, 0.80, 0.34, 1.00] },
        { d: 300, c: [0.34, 0.88, 0.34, 1.00] }
    ]
    readonly property var spec: specs[Math.max(0, Math.min(type, specs.length - 1))]

    duration: spec.d
    easing.type: Easing.BezierSpline
    easing.bezierCurve: spec.c.concat([1.0, 1.0]) // Qt wants groups of six numbers
}
