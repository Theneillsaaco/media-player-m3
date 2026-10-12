import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic
import "../Components"

// Settings: startup behaviour, the YouTube Music account and the Last.fm connection.
// The dialog polls the view model while it is open so progress shows up.
MDialog {
    id: dialog

    required property var vm
    property string browser: "firefox"
    property bool pasteOpen: false

    readonly property bool accountIn: vm.accountState === "signedIn"
    readonly property bool accountOut: vm.accountState === "signedOut" || vm.accountState === "expired"

    title: "Ajustes"

    Timer {
        interval: 500
        repeat: true
        running: dialog.visible
        onTriggered: dialog.vm.poll()
    }

    // ---- Al iniciar ----

    Text {
        text: "Al iniciar"
        color: "#6750A4"
        font.pixelSize: 14
        font.weight: Font.Medium
    }

    Text {
        Layout.fillWidth: true
        text: "La cola siempre vuelve en pausa."
        color: "#49454F"
        font.pixelSize: 12
        wrapMode: Text.Wrap
    }

    Flow {
        Layout.fillWidth: true
        spacing: 8

        Repeater {
            model: [
                { key: "continue", label: "Continuar donde lo dejé" },
                { key: "fromStart", label: "Cola, pista desde 0:00" },
                { key: "none", label: "No restaurar nada" }
            ]
            MChip {
                required property var modelData
                text: modelData.label
                selected: dialog.vm.startup === modelData.key
                onClicked: dialog.vm.chooseStartup(modelData.key)
            }
        }
    }

    Rectangle {
        Layout.fillWidth: true
        Layout.preferredHeight: 1
        color: "#CAC4D0"
    }

    // ---- YouTube Music ----

    Text {
        text: "YouTube Music"
        color: "#6750A4"
        font.pixelSize: 14
        font.weight: Font.Medium
    }

    Text {
        Layout.fillWidth: true
        text: dialog.vm.accountStatus
        color: "#49454F"
        font.pixelSize: 14
        wrapMode: Text.Wrap
    }

    Text {
        Layout.fillWidth: true
        visible: dialog.accountOut
        text: "Inicia sesión en music.youtube.com con ese navegador y elígelo aquí. La cookie equivale a tu contraseña: se guarda en el llavero (o en un archivo con permisos 600) y solo se envía a YouTube."
        color: "#49454F"
        font.pixelSize: 12
        wrapMode: Text.Wrap
    }

    Flow {
        Layout.fillWidth: true
        visible: dialog.accountOut
        spacing: 8

        Repeater {
            model: ["firefox", "chrome", "chromium", "brave", "vivaldi", "edge", "opera"]
            MChip {
                required property string modelData
                text: modelData.charAt(0).toUpperCase() + modelData.slice(1)
                selected: dialog.browser === modelData
                onClicked: dialog.browser = modelData
            }
        }
    }

    Rectangle {
        Layout.fillWidth: true
        Layout.preferredHeight: 96
        visible: dialog.accountOut && dialog.pasteOpen
        radius: 12
        color: "#F7F2FA"
        border.width: pasteArea.activeFocus ? 2 : 1
        border.color: pasteArea.activeFocus ? "#6750A4" : "#79747E"

        ScrollView {
            anchors.fill: parent
            anchors.margins: 8

            TextArea {
                id: pasteArea
                wrapMode: TextArea.WrapAnywhere
                selectByMouse: true
                font.pixelSize: 12
                color: "#1D1B20"
                placeholderText: "Pega aquí la cabecera cookie de music.youtube.com…"
                placeholderTextColor: "#49454F"
                background: null
            }
        }
    }

    RowLayout {
        Layout.fillWidth: true
        spacing: 8
        visible: dialog.accountOut

        Item { Layout.fillWidth: true }

        MButton {
            kind: "text"
            text: dialog.pasteOpen ? "Usar cookie" : "Pegar cookie"
            enabled: !dialog.vm.accountBusy && (!dialog.pasteOpen || pasteArea.text.length > 0)
            onClicked: {
                if (!dialog.pasteOpen) {
                    dialog.pasteOpen = true
                } else {
                    dialog.vm.useCookie(pasteArea.text)
                    pasteArea.text = ""
                    dialog.pasteOpen = false
                }
            }
        }
        MButton {
            text: "Importar"
            enabled: !dialog.vm.accountBusy
            onClicked: dialog.vm.importBrowserSession(dialog.browser)
        }
    }

    RowLayout {
        Layout.fillWidth: true
        visible: dialog.accountIn

        Item { Layout.fillWidth: true }

        MButton {
            kind: "outlined"
            text: "Cerrar sesión"
            onClicked: dialog.vm.signOutAccount()
        }
    }

    Rectangle {
        Layout.fillWidth: true
        Layout.preferredHeight: 1
        color: "#CAC4D0"
    }

    // ---- Last.fm ----

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
        onToggled: dialog.vm.enableScrobbling(checked)

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
