import QtQuick

// Finds the dominant color of the cover and reports it as "#rrggbb" through found(). found("") means there is
// nothing usable (no cover, or a grey one): the interface goes back to the chosen accent.
// It paints the cover into a 32x32 Canvas placed outside the window and reads the pixels back, so it needs
// no extra library. Nothing happens while active is false. Main.qml sends the result to ThemeViewModel.
Item {
    id: root

    property string source: ""
    property bool active: false

    signal found(string hex)

    property string loadedSource: ""

    function sample() {
        if (!active)
            return
        if (source === "") {
            found("")
            return
        }
        if (loadedSource !== "" && loadedSource !== source)
            cv.unloadImage(loadedSource)
        loadedSource = source

        if (cv.isImageLoaded(source))
            cv.requestPaint()
        else
            cv.loadImage(source)
    }

    // The most colorful big group of pixels. Near black, near white and grey pixels are ignored.
    function dominant(data) {
        const buckets = ({})

        for (let i = 0; i < data.length; i += 4) {
            if (data[i + 3] < 200)
                continue
            const r = data[i], g = data[i + 1], b = data[i + 2]
            if (Math.max(r, g, b) < 40 || Math.min(r, g, b) > 235)
                continue

            const key = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4)
            let k = buckets[key]
            if (!k)
                k = buckets[key] = { n: 0, r: 0, g: 0, b: 0 }
            k.n++
            k.r += r
            k.g += g
            k.b += b
        }

        let best = null
        let bestScore = 0

        for (const key in buckets) {
            const k = buckets[key]
            const chroma = (Math.max(k.r, k.g, k.b) - Math.min(k.r, k.g, k.b)) / k.n
            if (chroma < 24)
                continue
            const score = k.n * (chroma / 255 + 0.25)
            if (score > bestScore) {
                best = k
                bestScore = score
            }
        }

        if (best === null)
            return ""

        const hex = (v) => Math.round(v / best.n).toString(16).padStart(2, "0")
        return "#" + hex(best.r) + hex(best.g) + hex(best.b)
    }

    onSourceChanged: sample()
    onActiveChanged: sample()
    Component.onCompleted: sample()

    Canvas {
        id: cv
        x: -64
        y: -64
        width: 32
        height: 32
        renderTarget: Canvas.Image

        onImageLoaded: {
            if (root.active && root.source !== "" && isImageLoaded(root.source))
                requestPaint()
        }

        onPaint: {
            if (!root.active || root.source === "" || !isImageLoaded(root.source))
                return
            const ctx = getContext("2d")
            ctx.clearRect(0, 0, 32, 32)
            ctx.drawImage(root.source, 0, 0, 32, 32)
            root.found(root.dominant(ctx.getImageData(0, 0, 32, 32).data))
        }
    }
}
