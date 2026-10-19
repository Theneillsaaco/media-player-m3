import QtQuick
import QtQuick.Controls.Basic

Slider {
    id: slider

    readonly property var theme: Window.window ? Window.window.theme : null

    property bool expressive: false
    property bool playing: false
    property string valueLabel: ""

    property color cPrimary: theme ? theme.primary : "#6750A4"
    property color cSecondaryContainer: theme ? theme.secondaryContainer : "#E8DEF8"
    property color cDisabled: "#A0A0A0"

    signal changedByUser(real v)

    readonly property real trackHeight: 4
    readonly property real waveHeight: 16
    readonly property real fullHandleHeight: 20
    readonly property real handleWidth: 4
    readonly property real handlePressedWidth: 2
    readonly property real sliderGap: slider.expressive ? 6 : 0
    readonly property real stopSize: 4
    readonly property bool windowIsVisible: Window.window !== null && Window.window.visible
        && Window.window.visibility !== Window.Minimized

    implicitHeight: 24
    implicitWidth: 120
    opacity: enabled ? 1 : 0.38

    readonly property real thumbCenter: visualPosition * (availableWidth - handleWidth) + handleWidth / 2
    readonly property real trackEnd: availableWidth - stopSize - sliderGap

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
        
        Item {
            id: activeTrackContainer
            width: slider.expressive
                ? Math.max(0, slider.thumbCenter - slider.handleWidth / 2 - slider.sliderGap)
                : Math.max(0, slider.thumbCenter)
            height: parent.height
            
            Rectangle {
                anchors.fill: parent
                radius: height / 2
                topRightRadius: slider.expressive ? 2 : height / 2
                bottomRightRadius: slider.expressive ? 2 : height / 2
                color: slider.enabled ? slider.cPrimary : slider.cDisabled
                visible: waveCanvas.currentAmplitude < 0.1 || activeTrackContainer.width < 12
            }
            
            Canvas {
                id: waveCanvas
                anchors.centerIn: parent
                width: parent.width
                height: slider.waveHeight
                visible: activeTrackContainer.width >= 12 && currentAmplitude >= 0.01

                property real targetAmplitude: slider.playing ? 2.0 : 0.0
                property real currentAmplitude: 0.0
                property real phase: 0.0

                Behavior on currentAmplitude {
                    NumberAnimation {
                        duration: 150
                        easing.type: Easing.BezierSpline
                        easing.bezierCurve: [0.2, 0.0, 0.0, 1.0]
                    }
                }

                onTargetAmplitudeChanged: currentAmplitude = targetAmplitude

                renderTarget: Canvas.Image
                smooth: true

                onPaint: {
                    var ctx = getContext("2d");
                    ctx.clearRect(0, 0, width, height);

                    if (width <= 0) return;

                    var w = Math.floor(width);
                    var h = height;
                    var centerY = h / 2;
                    var wavelength = 48;

                    ctx.beginPath();
                    ctx.strokeStyle = slider.enabled ? slider.cPrimary : slider.cDisabled;
                    ctx.lineWidth = slider.trackHeight;
                    ctx.lineCap = "round";
                    ctx.lineJoin = "round";

                    var startX = Math.floor(slider.trackHeight / 2);
                    var endX = Math.floor(Math.max(startX, w - (slider.expressive ? 2 : slider.trackHeight / 2)));

                    ctx.moveTo(startX, centerY + Math.sin((startX / wavelength) * Math.PI * 2 + phase) * currentAmplitude);

                    for (var x = startX + 1; x <= endX; x++) {
                        var y = centerY + Math.sin((x / wavelength) * Math.PI * 2 + phase) * currentAmplitude;
                        ctx.lineTo(x, y);
                    }

                    ctx.stroke();
                }

                Timer {
                    running: (slider.playing || waveCanvas.currentAmplitude > 0.01) && slider.visible && slider.windowIsVisible
                    repeat: true
                    interval: 16
                    onTriggered: {
                        if (slider.playing) {
                            waveCanvas.phase += 0.05;
                        }
                        waveCanvas.requestPaint();
                    }
                }
            }
        }
        
        Rectangle {
            objectName: "sliderInactiveTrack"
            x: slider.expressive
                ? Math.min(parent.width, slider.thumbCenter + slider.handleWidth / 2 + slider.sliderGap)
                : Math.min(parent.width, slider.thumbCenter)
            width: Math.max(0, parent.width - x)
            height: parent.height
            radius: height / 2
            topLeftRadius: slider.expressive ? 2 : height / 2
            bottomLeftRadius: slider.expressive ? 2 : height / 2
            color: slider.enabled ? slider.cSecondaryContainer : slider.cDisabled
        }
        
        Rectangle {
            objectName: "sliderStop"
            x: slider.trackEnd
            anchors.verticalCenter: parent.verticalCenter
            width: slider.stopSize
            height: slider.stopSize
            radius: height / 2
            color: slider.enabled ? slider.cPrimary : slider.cDisabled
            visible: slider.expressive && (slider.thumbCenter + slider.handleWidth / 2 + slider.sliderGap < slider.trackEnd)
        }
    }

    handle: Item {
        x: slider.leftPadding + slider.visualPosition * (slider.availableWidth - slider.handleWidth)
        y: slider.topPadding + (slider.availableHeight - height) / 2
        width: slider.handleWidth
        height: Math.min(slider.fullHandleHeight, slider.height)

        Rectangle {
            objectName: "sliderHandle"
            anchors.centerIn: parent
            width: (slider.expressive && (slider.pressed || slider.visualFocus))
                ? slider.handlePressedWidth
                : slider.handleWidth
            height: parent.height
            radius: width / 2
            color: slider.enabled ? slider.cPrimary : slider.cDisabled

            Behavior on width {
                enabled: slider.expressive
                NumberAnimation {
                    duration: 150
                    easing.type: Easing.BezierSpline
                    easing.bezierCurve: [0.2, 0.0, 0.0, 1.0]
                }
            }
        }
    }

    ToolTip {
        objectName: "sliderValueLabel"
        visible: slider.valueLabel.length > 0 && (slider.pressed || slider.visualFocus)
        delay: 0
        timeout: -1
        x: slider.leftPadding + slider.visualPosition * (slider.availableWidth - width)
        y: -height - 12
        padding: 8
        contentItem: Text {
            text: slider.valueLabel
            color: "#FFFFFF"
            font.pixelSize: 12
        }
        background: Rectangle {
            color: "#313033"
            radius: height / 2
        }
    }
}