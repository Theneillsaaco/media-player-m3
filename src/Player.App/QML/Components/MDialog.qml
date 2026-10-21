import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic

// Material 3 basic dialog: surface container high, 28dp corners, 24dp padding, 32% scrim,
// headline-small title. Put the content (and the action buttons) inside it as children;
// it scrolls when the window is too short for it.
// Imports the Basic style explicitly so no system style can change it.
Popup {
    id: dialog

    property string title: ""
    // The window's theme through the overlay it sits on; a dialog can also be given one explicitly.
    property var theme: parent && parent.Window.window ? parent.Window.window.theme : null
    property var i18n: parent && parent.Window.window ? parent.Window.window.i18n : null
    property color cSurface: theme ? theme.surfaceContainerHigh : "#ECE6F0"
    property color cOnSurface: theme ? theme.inkSurface : "#1D1B20"
    default property alias content: body.data

    function tr(key) { return i18n ? i18n.t(key) : key }

    parent: Overlay.overlay
    anchors.centerIn: parent
    width: Math.min(parent ? parent.width - 48 : 440, 440)
    height: Math.min(implicitHeight, parent ? parent.height - 48 : implicitHeight)
    modal: true
    focus: true
    padding: 24
    closePolicy: Popup.CloseOnEscape | Popup.CloseOnPressOutside

    Overlay.modal: Rectangle { color: Qt.rgba(0, 0, 0, 0.32) }

    readonly property int cMotionIn: theme ? theme.durationMedium1 : 250
    readonly property int cMotionOut: theme ? theme.durationShort3 : 150
    readonly property var cEaseIn: theme ? theme.easingEmphasizedDecelerate : [0.05, 0.7, 0.1, 1.0, 1.0, 1.0]
    readonly property var cEaseOut: theme ? theme.easingEmphasizedAccelerate : [0.3, 0.0, 0.8, 0.15, 1.0, 1.0]

    enter: Transition {
        ParallelAnimation {
            NumberAnimation { property: "opacity"; from: 0; to: 1; duration: dialog.cMotionIn; easing.type: Easing.BezierSpline; easing.bezierCurve: dialog.cEaseIn }
            NumberAnimation { property: "scale"; from: 0.92; to: 1; duration: dialog.cMotionIn; easing.type: Easing.BezierSpline; easing.bezierCurve: dialog.cEaseIn }
        }
    }
    exit: Transition {
        NumberAnimation { property: "opacity"; from: 1; to: 0; duration: dialog.cMotionOut; easing.type: Easing.BezierSpline; easing.bezierCurve: dialog.cEaseOut }
    }

    background: Rectangle {
        color: dialog.cSurface
        radius: 28
    }

    contentItem: ColumnLayout {
        spacing: 16

        Text {
            Layout.fillWidth: true
            visible: dialog.title.length > 0
            text: dialog.title
            color: dialog.cOnSurface
            font.pixelSize: 24
            wrapMode: Text.Wrap
        }

        Flickable {
            Layout.fillWidth: true
            Layout.fillHeight: true
            Layout.preferredHeight: body.implicitHeight
            contentWidth: width
            contentHeight: body.implicitHeight
            clip: true
            boundsBehavior: Flickable.StopAtBounds
            ScrollBar.vertical: ScrollBar {}

            ColumnLayout {
                id: body
                width: parent.width
                spacing: 16
            }
        }
    }
}
