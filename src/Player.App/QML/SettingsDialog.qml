import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic

// Settings. For now: the Last.fm connection and the scrobbling switch.
// The dialog polls the view model while it is open so sign-in progress shows up.
MDialog {
    id: dialog

    required property var vm
    title: "Ajustes"

    Timer {
        interval: 500
        repeat: true
        running: dialog.visible
        onTriggered: dialog.vm.poll()
    }

    Text {
        text: "Last.fm"
        color: "#6750A4"
        font.pixelSize: 14
        font.weight: Font.Medium
    }

    Text {
        Layout.fillWidth: true
        text: dialog.vm.scrobbleStatus
        color: "#49454F"
        font.pixelSize: 14
        wrapMode: Text.Wrap
    }

    Text {
        Layout.fillWidth: true
        visible: dialog.vm.scrobbleState === "unavailable"
        text: "Registra una app en last.fm/api y guarda la clave en ~/.config/player/lastfm.json como {\"apiKey\": \"...\", \"secret\": \"...\"}, o con las variables PLAYER_LASTFM_KEY y PLAYER_LASTFM_SECRET."
        color: "#49454F"
        font.pixelSize: 12
        wrapMode: Text.Wrap
    }

    // Waiting for the browser authorisation: the link is shown in case the browser did not open.
    TextEdit {
        Layout.fillWidth: true
        visible: dialog.vm.scrobbleState === "authorizing"
        text: dialog.vm.scrobbleAuthUrl
        readOnly: true
        selectByMouse: true
        wrapMode: TextEdit.WrapAnywhere
        color: "#49454F"
        font.pixelSize: 12
    }

    MSwitch {
        id: sendSwitch
        Layout.fillWidth: true
        visible: dialog.vm.scrobbleState === "signedIn"
        text: "Enviar scrobbles"
        onToggled: dialog.vm.setScrobbleEnabled(checked)

        // A Binding keeps following the model after the user has toggled the switch.
        Binding {
            target: sendSwitch
            property: "checked"
            value: dialog.vm.scrobbleEnabled
        }
    }

    RowLayout {
        Layout.fillWidth: true
        spacing: 8

        Item { Layout.fillWidth: true }

        MButton {
            visible: dialog.vm.scrobbleState === "authorizing"
            kind: "text"
            text: "Cancelar"
            enabled: !dialog.vm.scrobbleBusy
            onClicked: dialog.vm.cancelScrobblerSignIn()
        }
        MButton {
            visible: dialog.vm.scrobbleState === "authorizing"
            text: "Continuar"
            enabled: !dialog.vm.scrobbleBusy
            onClicked: dialog.vm.finishScrobblerSignIn()
        }
        MButton {
            visible: dialog.vm.scrobbleState === "signedOut" || dialog.vm.scrobbleState === "reconnect"
            text: "Conectar"
            enabled: !dialog.vm.scrobbleBusy
            onClicked: dialog.vm.connectScrobbler()
        }
        MButton {
            visible: dialog.vm.scrobbleState === "signedIn" && dialog.vm.scrobblePending > 0
            kind: "tonal"
            text: "Reintentar"
            onClicked: dialog.vm.retryScrobbles()
        }
        MButton {
            visible: dialog.vm.scrobbleState === "signedIn"
            kind: "outlined"
            text: "Desconectar"
            onClicked: dialog.vm.disconnectScrobbler()
        }
    }

    RowLayout {
        Layout.fillWidth: true
        Item { Layout.fillWidth: true }
        MButton {
            kind: "text"
            text: "Cerrar"
            onClicked: dialog.close()
        }
    }
}
