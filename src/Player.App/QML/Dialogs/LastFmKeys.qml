import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic
import "../Components"

// Asks for the Last.fm API key and shared secret while none is configured (Settings > Last.fm). They are saved
// to ~/.config/player/lastfm.json and used at once. vm is the SettingsViewModel; theme the window's Theme.
ColumnLayout {
    id: root

    required property var vm
    required property var theme

    spacing: 8

    component Field: TextField {
        Layout.fillWidth: true
        implicitHeight: 48
        placeholderTextColor: root.theme.onSurfaceVariant
        color: root.theme.onSurface
        font.pixelSize: 14
        selectByMouse: true
        leftPadding: 16
        rightPadding: 16
        verticalAlignment: TextInput.AlignVCenter
        background: Rectangle {
            radius: 8
            color: "transparent"
            border.width: parent.activeFocus ? 2 : 1
            border.color: parent.activeFocus ? root.theme.primary : root.theme.outline
        }
    }

    Text {
        Layout.fillWidth: true
        text: "Crea una app en last.fm/api/account/create y pega aquí su clave de API y su secreto compartido. Se guardan en ~/.config/player/lastfm.json, que solo tú puedes leer."
        color: root.theme.onSurfaceVariant
        font.pixelSize: 12
        wrapMode: Text.Wrap
    }

    Field {
        id: keyField
        placeholderText: "Clave de API"
    }

    Field {
        id: secretField
        placeholderText: "Secreto compartido"
        echoMode: TextInput.Password
    }

    RowLayout {
        Layout.fillWidth: true

        Item { Layout.fillWidth: true }

        MButton {
            text: "Guardar"
            enabled: keyField.text.trim().length > 0 && secretField.text.trim().length > 0
            onClicked: {
                root.vm.saveScrobbleKeys(keyField.text, secretField.text)
                secretField.text = ""
            }
        }
    }
}
