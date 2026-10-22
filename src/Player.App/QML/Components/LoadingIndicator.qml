import QtQuick
import QtQuick.Shapes

// Material 3 Expressive loading indicator: one shape that keeps turning into the next one (soft burst, cookie,
// pentagon, pill, sunny, four-sided cookie, oval) while it spins. Every shape is a radius per angle, the morph
// is a blend of two of those lists with a little overshoot, so it costs one animated number and one polygon.
// It does nothing while it is not visible. contained: round container behind it, as M3 draws it.
Item {
    id: root

    readonly property var theme: Window.window ? Window.window.theme : null

    property bool running: true
    property bool contained: false
    property real size: 48
    property color color: contained ? (theme ? theme.inkPrimaryContainer : "#4d3d75") : (theme ? theme.primary : "#65558f")
    property color containerColor: theme ? theme.primaryContainer : "#e9ddff"

    implicitWidth: size
    implicitHeight: size

    readonly property int samples: 72
    readonly property var shapes: buildShapes()
    property real phase: 0 // 0 .. shapes.length: the integer part is the shape, the rest how far the morph has gone

    // ---- shapes: radius of the outline at an angle ----

    function polygon(n, a) {
        const sector = 2 * Math.PI / n
        const local = ((a % sector) + sector) % sector - sector / 2
        return Math.cos(Math.PI / n) / Math.cos(local)
    }

    function superellipse(p, a) {
        return Math.pow(Math.pow(Math.abs(Math.cos(a)), p) + Math.pow(Math.abs(Math.sin(a)), p), -1 / p)
    }

    function ellipse(x, y, a) {
        return x * y / Math.hypot(y * Math.cos(a), x * Math.sin(a))
    }

    // Stadium shape: half length `half`, half width `wide`, along x. The ray is bisected to the edge.
    function capsule(half, wide, a) {
        const s = half - wide
        const dx = Math.cos(a), dy = Math.sin(a)
        let lo = 0, hi = half * 2
        for (let i = 0; i < 30; i++) {
            const t = (lo + hi) / 2
            const x = Math.abs(t * dx), y = t * dy
            const d = x <= s ? Math.abs(y) : Math.hypot(x - s, y)
            if (d <= wide) lo = t; else hi = t
        }
        return lo
    }

    function raw(kind, a) {
        switch (kind) {
        case 0: return 1 + 0.12 * Math.cos(10 * a)                                      // soft burst
        case 1: return 1 + 0.09 * (2 * Math.abs(Math.cos(4.5 * a)) - 1)                 // cookie, 9 lobes
        case 2: return polygon(5, a)                                                    // pentagon
        case 3: return capsule(1.0, 0.6, a)                                             // pill
        case 4: return 0.72 + 0.28 * Math.pow(0.5 + 0.5 * Math.cos(8 * a), 1.3)         // sunny
        case 5: return superellipse(3.2, a)                                             // cookie, 4 sides
        default: return ellipse(1.0, 0.7, a)                                            // oval
        }
    }

    // Done once: sample, soften the corners and give every shape the same area so none looks bigger.
    function buildShapes() {
        const n = samples
        const weights = [1, 2, 3, 2, 1]
        const list = []

        for (let kind = 0; kind < 7; kind++) {
            const r = []
            for (let i = 0; i < n; i++)
                r.push(raw(kind, 2 * Math.PI * i / n))

            const soft = []
            let mean = 0
            for (let i = 0; i < n; i++) {
                let sum = 0
                for (let k = -2; k <= 2; k++)
                    sum += r[(i + k + n) % n] * weights[k + 2]
                soft.push(sum / 9)
                mean += (sum / 9) * (sum / 9)
            }

            const scale = 0.68 / Math.sqrt(mean / n)
            list.push(soft.map(v => v * scale))
        }
        return list
    }

    // ---- the outline at one moment ----

    function makePoints(ph) {
        const n = shapes.length
        const m = ((ph % n) + n) % n
        const i = Math.floor(m)
        const f = m - i
        const x = f < 0.3 ? 0 : (f - 0.3) / 0.7 // a short hold, then the morph
        const c = 1.15
        const t = x === 0 ? 0 : 1 + (c + 1) * Math.pow(x - 1, 3) + c * Math.pow(x - 1, 2) // ease out with overshoot
        const a = shapes[i], b = shapes[(i + 1) % n]
        const spin = ph * 2 * Math.PI / n // one turn per cycle, so the loop has no jump
        const half = size / 2
        const k = contained ? 0.78 : 1
        const out = []

        for (let s = 0; s < samples; s++) {
            const r = (a[s] + (b[s] - a[s]) * t) * half * k
            const ang = 2 * Math.PI * s / samples + spin
            out.push(Qt.point(half + r * Math.cos(ang), half + r * Math.sin(ang)))
        }
        return out
    }

    readonly property var points: makePoints(phase)

    NumberAnimation on phase {
        from: 0
        to: root.shapes.length
        duration: root.shapes.length * 700
        loops: Animation.Infinite
        running: root.running && root.visible
    }

    Rectangle {
        anchors.fill: parent
        radius: width / 2
        color: root.containerColor
        visible: root.contained
    }

    Shape {
        anchors.fill: parent
        preferredRendererType: Shape.CurveRenderer

        ShapePath {
            fillColor: root.color
            strokeColor: "transparent"
            PathPolyline { path: root.points }
        }
    }
}
