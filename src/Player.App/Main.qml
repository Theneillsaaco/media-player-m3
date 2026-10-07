import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic

ApplicationWindow {
    id: win
    width: 520
    height: 300
    minimumWidth: 400
    minimumHeight: 260
    visible: true
    title: "Player"
    color: "#FEF7FF" // M3 baseline surface

    PlayerViewModel { id: player }

    // Polls only while something is playing, so an idle player wakes nothing.
    Timer {
        interval: 250
        repeat: true
        running: player.loaded && !player.paused
        onTriggered: player.poll()
    }

    function clock(s) {
        const t = Math.max(0, Math.floor(s))
        return Math.floor(t / 60) + ":" + String(t % 60).padStart(2, "0")
    }

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: 24
        spacing: 16

        TextField {
            id: input
            Layout.fillWidth: true
            placeholderText: "Enlace de YouTube, stream o ruta de archivo"
            placeholderTextColor: "#49454F"
            color: "#1D1B20"
            font.pixelSize: 16
            selectByMouse: true
            onAccepted: player.load(text)
            background: Rectangle {
                radius: 4
                color: "transparent"
                border.width: input.activeFocus ? 2 : 1
                border.color: input.activeFocus ? "#6750A4" : "#79747E"
            }
        }

        RowLayout {
            spacing: 8
            MButton { text: "Cargar"; kind: "filled"; onClicked: player.load(input.text) }
            MButton {
                text: player.paused ? "Reproducir" : "Pausar"
                kind: "tonal"
                enabled: player.loaded
                onClicked: player.toggle()
            }
        }

        Item { // seek bar
            id: bar
            Layout.fillWidth: true
            implicitHeight: 24
            enabled: player.loaded && player.duration > 0
            readonly property real fraction:
                player.duration > 0 ? Math.min(1, player.position / player.duration) : 0

            Rectangle {
                anchors.verticalCenter: parent.verticalCenter
                width: parent.width
                height: 4
                radius: 2
                color: "#E6E0E9"
                Rectangle { width: parent.width * bar.fraction; height: parent.height; radius: 2; color: "#6750A4" }
            }
            Rectangle {
                anchors.verticalCenter: parent.verticalCenter
                x: (bar.width - width) * bar.fraction
                width: 4
                height: 20
                radius: 2
                color: "#6750A4"
                visible: bar.enabled
            }
            MouseArea {
                anchors.fill: parent
                onClicked: (m) => player.seek(Math.max(0, Math.min(1, m.x / width)) * player.duration)
            }
        }

        RowLayout {
            Layout.fillWidth: true
            Text { text: win.clock(player.position); color: "#49454F"; font.pixelSize: 12 }
            Item { Layout.fillWidth: true }
            Text { text: win.clock(player.duration); color: "#49454F"; font.pixelSize: 12 }
        }

        RowLayout {
            Layout.fillWidth: true
            Text { text: "Volumen"; color: "#49454F"; font.pixelSize: 12 }
            Slider {
                Layout.fillWidth: true
                from: 0
                to: 100
                value: 100
                onMoved: player.setVolume(Math.round(value))
            }
        }

        Text {
            Layout.fillWidth: true
            text: player.status
            color: "#49454F"
            font.pixelSize: 14
            wrapMode: Text.Wrap
        }
        Item { Layout.fillHeight: true }
    }
}
