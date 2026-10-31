import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic
import "../Components"

// Settings: language, appearance, startup behaviour, the YouTube Music account and the Last.fm connection.
// The dialog polls the view model while it is open so progress shows up.
MDialog {
    id: dialog

    required property var vm
    required property var appearanceVm
    required property var videoVm
    required property var localeVm
    property string browser: "firefox"
    property bool pasteOpen: false

    readonly property bool accountIn: vm.accountState === "signedIn"
    readonly property bool accountOut: vm.accountState === "signedOut" || vm.accountState === "expired"

    title: dialog.tr("settings.title")

    Timer {
        interval: 500
        repeat: true
        running: dialog.visible
        onTriggered: dialog.vm.poll()
    }

    // ---- Idioma ----

    Text {
        text: dialog.tr("settings.language")
        color: dialog.theme.primary
        font.pixelSize: 14
        font.weight: Font.Medium
    }

    Flow {
        Layout.fillWidth: true
        spacing: 8

        Repeater {
            model: [
                { key: "es", label: "language.es" },
                { key: "en", label: "language.en" }
            ]
            MChip {
                required property var modelData
                text: dialog.tr(modelData.label)
                selected: dialog.localeVm.language === modelData.key
                onClicked: dialog.localeVm.chooseLanguage(modelData.key)
            }
        }
    }

    Rectangle {
        Layout.fillWidth: true
        Layout.preferredHeight: 1
        color: dialog.theme.outlineVariant
    }

    // ---- Apariencia ----

    AppearanceSection {
        Layout.fillWidth: true
        vm: dialog.appearanceVm
        theme: dialog.theme
    }

    Rectangle {
        Layout.fillWidth: true
        Layout.preferredHeight: 1
        color: dialog.theme.outlineVariant
    }

    // ---- Al iniciar ----

    Text {
        text: dialog.tr("settings.startup")
        color: dialog.theme.primary
        font.pixelSize: 14
        font.weight: Font.Medium
    }

    Text {
        Layout.fillWidth: true
        text: dialog.tr("settings.startupNote")
        color: dialog.theme.inkSurfaceVariant
        font.pixelSize: 12
        wrapMode: Text.Wrap
    }

    Flow {
        Layout.fillWidth: true
        spacing: 8

        Repeater {
            model: [
                { key: "continue", label: "startup.continue" },
                { key: "fromStart", label: "startup.fromStart" },
                { key: "none", label: "startup.none" }
            ]
            MChip {
                required property var modelData
                text: dialog.tr(modelData.label)
                selected: dialog.vm.startup === modelData.key
                onClicked: dialog.vm.chooseStartup(modelData.key)
            }
        }
    }

    Rectangle {
        Layout.fillWidth: true
        Layout.preferredHeight: 1
        color: dialog.theme.outlineVariant
    }

    // ---- Reproducción ----

    Text {
        text: dialog.tr("settings.playback")
        color: dialog.theme.primary
        font.pixelSize: 14
        font.weight: Font.Medium
    }

    MSwitch {
        id: autoPlaySwitch
        Layout.fillWidth: true
        text: dialog.tr("settings.autoplay")
        onToggled: dialog.vm.chooseAutoPlay(checked)

        // A Binding keeps following the model after the user has toggled the switch.
        Binding {
            target: autoPlaySwitch
            property: "checked"
            value: dialog.vm.autoPlay
        }
    }

    MSwitch {
        id: normalizeSwitch
        Layout.fillWidth: true
        text: dialog.tr("settings.normalize")
        onToggled: dialog.vm.chooseNormalizeVolume(checked)

        Binding {
            target: normalizeSwitch
            property: "checked"
            value: dialog.vm.normalizeVolume
        }
    }

    Rectangle {
        Layout.fillWidth: true
        Layout.preferredHeight: 1
        color: dialog.theme.outlineVariant
    }

    // ---- Video ----

    Text {
        text: dialog.tr("settings.video")
        color: dialog.theme.primary
        font.pixelSize: 14
        font.weight: Font.Medium
    }

    MSwitch {
        id: autoVideo
        Layout.fillWidth: true
        text: dialog.tr("settings.videoAuto")
        onToggled: dialog.videoVm.chooseAuto(checked)

        // A Binding keeps following the model after the user has toggled the switch.
        Binding {
            target: autoVideo
            property: "checked"
            value: dialog.videoVm.autoShow
        }
    }

    Rectangle {
        Layout.fillWidth: true
        Layout.preferredHeight: 1
        color: dialog.theme.outlineVariant
    }

    // ---- YouTube Music ----

    Text {
        text: "YouTube Music"
        color: dialog.theme.primary
        font.pixelSize: 14
        font.weight: Font.Medium
    }

    Text {
        Layout.fillWidth: true
        text: dialog.vm.accountStatus
        color: dialog.theme.inkSurfaceVariant
        font.pixelSize: 14
        wrapMode: Text.Wrap
    }

    Text {
        Layout.fillWidth: true
        visible: dialog.accountOut
        text: dialog.tr("ytm.help")
        color: dialog.theme.inkSurfaceVariant
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
        color: dialog.theme.surfaceContainerLow
        border.width: pasteArea.activeFocus ? 2 : 1
        border.color: pasteArea.activeFocus ? dialog.theme.primary : dialog.theme.outline

        ScrollView {
            anchors.fill: parent
            anchors.margins: 8

            TextArea {
                id: pasteArea
                wrapMode: TextArea.WrapAnywhere
                selectByMouse: true
                font.pixelSize: 12
                color: dialog.theme.inkSurface
                placeholderText: dialog.tr("ytm.cookiePlaceholder")
                placeholderTextColor: dialog.theme.inkSurfaceVariant
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
            text: dialog.pasteOpen ? dialog.tr("action.useCookie") : dialog.tr("action.pasteCookie")
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
            text: dialog.tr("action.import")
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
            text: dialog.tr("action.signOut")
            onClicked: dialog.vm.signOutAccount()
        }
    }

    Rectangle {
        Layout.fillWidth: true
        Layout.preferredHeight: 1
        color: dialog.theme.outlineVariant
    }

    // ---- Last.fm ----

    Text {
        text: "Last.fm"
        color: dialog.theme.primary
        font.pixelSize: 14
        font.weight: Font.Medium
    }

    Text {
        Layout.fillWidth: true
        text: dialog.vm.scrobbleStatus
        color: dialog.theme.inkSurfaceVariant
        font.pixelSize: 14
        wrapMode: Text.Wrap
    }

    // No API key yet: ask for it here instead of by environment variable.
    LastFmKeys {
        Layout.fillWidth: true
        visible: dialog.vm.scrobbleState === "unavailable"
        vm: dialog.vm
        theme: dialog.theme
    }

    // Waiting for the browser authorisation: the link is shown in case the browser did not open.
    TextEdit {
        Layout.fillWidth: true
        visible: dialog.vm.scrobbleState === "authorizing"
        text: dialog.vm.scrobbleAuthUrl
        readOnly: true
        selectByMouse: true
        wrapMode: TextEdit.WrapAnywhere
        color: dialog.theme.inkSurfaceVariant
        font.pixelSize: 12
    }

    MSwitch {
        id: sendSwitch
        Layout.fillWidth: true
        visible: dialog.vm.scrobbleState === "signedIn"
        text: dialog.tr("lastfm.send")
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
            text: dialog.tr("action.cancel")
            enabled: !dialog.vm.scrobbleBusy
            onClicked: dialog.vm.cancelScrobblerSignIn()
        }
        MButton {
            visible: dialog.vm.scrobbleState === "authorizing"
            text: dialog.tr("action.continue")
            enabled: !dialog.vm.scrobbleBusy
            onClicked: dialog.vm.finishScrobblerSignIn()
        }
        MButton {
            visible: dialog.vm.scrobbleState === "signedOut" || dialog.vm.scrobbleState === "reconnect"
            text: dialog.tr("action.connect")
            enabled: !dialog.vm.scrobbleBusy
            onClicked: dialog.vm.connectScrobbler()
        }
        MButton {
            visible: dialog.vm.scrobbleState === "signedIn" && dialog.vm.scrobblePending > 0
            kind: "tonal"
            text: dialog.tr("action.retry")
            onClicked: dialog.vm.retryScrobbles()
        }
        MButton {
            visible: dialog.vm.scrobbleState === "signedIn"
            kind: "outlined"
            text: dialog.tr("action.disconnect")
            onClicked: dialog.vm.disconnectScrobbler()
        }
    }

    RowLayout {
        Layout.fillWidth: true
        Item { Layout.fillWidth: true }
        MButton {
            kind: "text"
            text: dialog.tr("action.close")
            onClicked: dialog.close()
        }
    }
}
