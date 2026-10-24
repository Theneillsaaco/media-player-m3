import QtQuick
import QtQuick.Shapes

// Rounds a picture without a mask: four small corner pieces, in the color of whatever is behind the picture, are
// drawn over it. A mask (MultiEffect) renders the picture into a texture and again through the mask on every frame,
// and a video is a new picture every frame; four triangles cost nothing. Put it over the picture, same size, with
// the radius of the picture and the color of the surface behind it.
Shape {
    id: root

    property real radius: 8
    property color color: "#000000"

    preferredRendererType: Shape.CurveRenderer

    readonly property real r: Math.max(0, Math.min(radius, width / 2, height / 2))
    readonly property string corners:
        "M0,0 L" + r + ",0 A" + r + "," + r + " 0 0 0 0," + r + " Z " +
        "M" + width + ",0 L" + width + "," + r + " A" + r + "," + r + " 0 0 0 " + (width - r) + ",0 Z " +
        "M" + width + "," + height + " L" + (width - r) + "," + height + " A" + r + "," + r + " 0 0 0 " + width + "," + (height - r) + " Z " +
        "M0," + height + " L0," + (height - r) + " A" + r + "," + r + " 0 0 0 " + r + "," + height + " Z"

    ShapePath {
        strokeColor: "transparent"
        fillColor: root.color
        PathSvg { path: root.corners }
    }
}
