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
    readonly property var i18n: Window.window ? Window.window.i18n : null

    function tr(key) { return i18n ? i18n.t(key) : key }

    spacing: 8

    component Field: TextField {
        Layout.fillWidth: true
        implicitHeight: 48
        placeholderTextColor: root.theme.inkSurfaceVariant
        color: root.theme.inkSurface
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
        text: root.tr("lastfm.keysHelp")
        color: root.theme.inkSurfaceVariant
        font.pixelSize: 12
        wrapMode: Text.Wrap
    }

    Field {
        id: keyField
        placeholderText: root.tr("lastfm.apiKey")
    }

    Field {
        id: secretField
        placeholderText: root.tr("lastfm.secret")
        echoMode: TextInput.Password
    }

    RowLayout {
        Layout.fillWidth: true

        Item { Layout.fillWidth: true }

        MButton {
            text: root.tr("action.save")
            enabled: keyField.text.trim().length > 0 && secretField.text.trim().length > 0
            onClicked: {
                root.vm.saveScrobbleKeys(keyField.text, secretField.text)
                secretField.text = ""
            }
        }
    }
}
