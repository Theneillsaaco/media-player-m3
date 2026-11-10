import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic
import "../Components"
import "../Dialogs"

// Settings, over the whole window: the sections on the left, the page of the chosen one on the right.
// Same API as the old dialog: open(), close() and visible. The view model is polled while it is open.
Rectangle {
    id: root

    required property var vm
    required property var appearanceVm
    required property var videoVm
    required property var localeVm
    required property var lyricsVm
    required property var theme
    readonly property var i18n: Window.window ? Window.window.i18n : null

    property bool opened: false
    property string section: "general"
    property string browser: "firefox"
    property bool pasteOpen: false

    readonly property bool accountIn: vm.accountState === "signedIn"
    readonly property bool accountOut: vm.accountState === "signedOut" || vm.accountState === "expired"
    readonly property bool narrow: width < 820

    readonly property var sections: [
        { key: "general", glyph: "settings", label: "settings.section.general" },
        { key: "appearance", glyph: "palette", label: "appearance.title" },
        { key: "playback", glyph: "play", label: "settings.playback" },
        { key: "lyrics", glyph: "lyrics", label: "settings.lyrics" },
        { key: "accounts", glyph: "person", label: "settings.section.accounts" }
    ]

    function tr(key) { return i18n ? i18n.t(key) : key }
    function open(page) {
        if (page) section = page
        opened = true
        forceActiveFocus()
    }
    function close() { opened = false }

    color: theme.surface
    opacity: opened ? 1 : 0
    visible: opacity > 0
    Behavior on opacity { Anim { type: Anim.FastEffects } }

    Timer {
        interval: 500
        repeat: true
        running: root.opened
        onTriggered: root.vm.poll()
    }

    Shortcut {
        sequence: "Esc"
        enabled: root.opened
        onActivated: root.close()
    }

    // Nothing under the page receives clicks or scrolling.
    MouseArea {
        anchors.fill: parent
        hoverEnabled: true
        acceptedButtons: Qt.AllButtons
        onWheel: (wheel) => wheel.accepted = true
    }

    // ---- building blocks ----

    component Pane: Flickable {
        default property alias content: column.data

        clip: true
        contentWidth: width
        contentHeight: column.implicitHeight + 48
        boundsBehavior: Flickable.StopAtBounds
        ScrollBar.vertical: ScrollBar {}

        ColumnLayout {
            id: column
            x: Math.max(24, (parent.width - width) / 2)
            y: 8
            width: Math.min(720, parent.width - 48)
            spacing: 16
        }
    }

    component Card: Rectangle {
        default property alias content: inner.data

        Layout.fillWidth: true
        radius: 20
        color: root.theme.surfaceContainer
        implicitHeight: inner.implicitHeight + 32

        ColumnLayout {
            id: inner
            x: 16
            y: 16
            width: parent.width - 32
            spacing: 12
        }
    }

    component Heading: Text {
        color: root.theme.primary
        font.pixelSize: 14
        font.weight: Font.Medium
    }

    component Note: Text {
        Layout.fillWidth: true
        color: root.theme.inkSurfaceVariant
        font.pixelSize: 12
        wrapMode: Text.Wrap
    }

    // ---- layout ----

    RowLayout {
        anchors.fill: parent
        spacing: 0

        Rectangle {
            Layout.fillHeight: true
            Layout.preferredWidth: root.narrow ? 88 : 300
            color: root.theme.surfaceContainerLow

            ColumnLayout {
                anchors.fill: parent
                anchors.margins: 12
                spacing: 4

                RowLayout {
                    Layout.fillWidth: true
                    Layout.bottomMargin: 12
                    spacing: 8

                    MIconButton {
                        glyph: "back"
                        tip: root.tr("action.close")
                        onClicked: root.close()
                    }
                    Text {
                        visible: !root.narrow
                        Layout.fillWidth: true
                        text: root.tr("settings.title")
                        color: root.theme.inkSurface
                        font.pixelSize: 24
                    }
                }

                Repeater {
                    model: root.sections

                    delegate: AbstractButton {
                        id: item

                        required property var modelData
                        readonly property bool active: root.section === modelData.key

                        Layout.fillWidth: true
                        implicitHeight: 56
                        hoverEnabled: true
                        focusPolicy: Qt.StrongFocus
                        Accessible.name: root.tr(modelData.label)
                        onClicked: root.section = modelData.key

                        background: Rectangle {
                            radius: 28
                            color: item.active ? root.theme.secondaryContainer : "transparent"

                            Rectangle { // state layer
                                anchors.fill: parent
                                radius: parent.radius
                                color: root.theme.inkSurface
                                opacity: item.active ? 0 : item.down ? 0.10 : item.hovered || item.visualFocus ? 0.08 : 0
                            }
                        }

                        contentItem: RowLayout {
                            spacing: 12

                            Item { Layout.fillWidth: root.narrow; Layout.preferredWidth: root.narrow ? 0 : 16 }
                            Icon {
                                name: item.modelData.glyph
                                size: 24
                                tint: item.active ? root.theme.inkSecondaryContainer : root.theme.inkSurfaceVariant
                            }
                            Text {
                                visible: !root.narrow
                                Layout.fillWidth: true
                                text: root.tr(item.modelData.label)
                                color: item.active ? root.theme.inkSecondaryContainer : root.theme.inkSurface
                                font.pixelSize: 16
                                font.weight: item.active ? Font.Medium : Font.Normal
                                elide: Text.ElideRight
                            }
                            Item { Layout.fillWidth: root.narrow }
                        }
                    }
                }

                Item { Layout.fillHeight: true }
            }
        }

        Item {
            Layout.fillWidth: true
            Layout.fillHeight: true

            Text {
                id: pageTitle
                x: 32
                y: 24
                text: root.tr(root.sections[stack.currentIndex].label)
                color: root.theme.inkSurface
                font.pixelSize: 28
            }

            StackLayout {
                id: stack
                anchors.top: pageTitle.bottom
                anchors.topMargin: 16
                anchors.left: parent.left
                anchors.right: parent.right
                anchors.bottom: parent.bottom
                currentIndex: Math.max(0, root.sections.findIndex(s => s.key === root.section))

                // ---- General ----
                Pane {
                    Card {
                        Heading { text: root.tr("settings.language") }
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
                                    text: root.tr(modelData.label)
                                    selected: root.localeVm.language === modelData.key
                                    onClicked: root.localeVm.chooseLanguage(modelData.key)
                                }
                            }
                        }
                    }
                    Card {
                        Heading { text: root.tr("settings.startup") }
                        Note { text: root.tr("settings.startupNote") }
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
                                    text: root.tr(modelData.label)
                                    selected: root.vm.startup === modelData.key
                                    onClicked: root.vm.chooseStartup(modelData.key)
                                }
                            }
                        }
                    }
                }

                // ---- Appearance ----
                Pane {
                    Card {
                        AppearanceSection {
                            Layout.fillWidth: true
                            vm: root.appearanceVm
                            theme: root.theme
                        }
                    }
                }

                // ---- Playback ----
                Pane {
                    Card {
                        Heading { text: root.tr("settings.playback") }
                        MSwitch {
                            id: autoPlaySwitch
                            Layout.fillWidth: true
                            text: root.tr("settings.autoplay")
                            onToggled: root.vm.chooseAutoPlay(checked)
                            Binding { target: autoPlaySwitch; property: "checked"; value: root.vm.autoPlay }
                        }
                        MSwitch {
                            id: normalizeSwitch
                            Layout.fillWidth: true
                            text: root.tr("settings.normalize")
                            onToggled: root.vm.chooseNormalizeVolume(checked)
                            Binding { target: normalizeSwitch; property: "checked"; value: root.vm.normalizeVolume }
                        }
                    }
                    Card {
                        Heading { text: root.tr("settings.cache") }
                        Note { text: root.tr("settings.cacheNote") }
                        Flow {
                            Layout.fillWidth: true
                            spacing: 8
                            Repeater {
                                model: [0, 10, 30, 100]
                                MChip {
                                    required property int modelData
                                    text: modelData === 0 ? root.tr("cache.off") : root.tr("cache.songs").replace("{0}", modelData)
                                    selected: root.vm.cacheLimit === modelData
                                    onClicked: root.vm.chooseCacheLimit(modelData)
                                }
                            }
                        }
                        RowLayout {
                            Layout.fillWidth: true
                            spacing: 8
                            visible: root.vm.cacheLimit > 0
                            Text {
                                Layout.fillWidth: true
                                text: root.vm.cacheSummary
                                color: root.theme.inkSurfaceVariant
                                font.pixelSize: 14
                                elide: Text.ElideRight
                            }
                            MButton {
                                kind: "outlined"
                                text: root.tr("cache.clear")
                                onClicked: root.vm.clearCache()
                            }
                        }
                    }
                    Card {
                        Heading { text: root.tr("settings.video") }
                        MSwitch {
                            id: autoVideo
                            Layout.fillWidth: true
                            text: root.tr("settings.videoAuto")
                            onToggled: root.videoVm.chooseAuto(checked)
                            Binding { target: autoVideo; property: "checked"; value: root.videoVm.autoShow }
                        }
                    }
                }

                // ---- Lyrics ----
                Pane {
                    Card {
                        Heading { text: root.tr("lyrics.extra") }
                        Flow {
                            Layout.fillWidth: true
                            spacing: 8
                            Repeater {
                                model: ["off", "romaji", "translation", "both"]
                                MChip {
                                    required property string modelData
                                    text: root.tr("lyrics.extra." + modelData)
                                    selected: root.lyricsVm.extraMode === modelData
                                    onClicked: root.lyricsVm.chooseExtra(modelData)
                                }
                            }
                        }
                        Note { text: root.tr("lyrics.extraNote") }
                    }
                }

                // ---- Accounts ----
                Pane {
                    Card {
                        Heading { text: "YouTube Music" }
                        Text {
                            Layout.fillWidth: true
                            text: root.vm.accountStatus
                            color: root.theme.inkSurfaceVariant
                            font.pixelSize: 14
                            wrapMode: Text.Wrap
                        }
                        Note { visible: root.accountOut; text: root.tr("ytm.help") }

                        Flow {
                            Layout.fillWidth: true
                            visible: root.accountOut
                            spacing: 8
                            Repeater {
                                model: ["firefox", "chrome", "chromium", "brave", "vivaldi", "edge", "opera"]
                                MChip {
                                    required property string modelData
                                    text: modelData.charAt(0).toUpperCase() + modelData.slice(1)
                                    selected: root.browser === modelData
                                    onClicked: root.browser = modelData
                                }
                            }
                        }

                        Rectangle {
                            Layout.fillWidth: true
                            Layout.preferredHeight: 96
                            visible: root.accountOut && root.pasteOpen
                            radius: 12
                            color: root.theme.surfaceContainerLow
                            border.width: pasteArea.activeFocus ? 2 : 1
                            border.color: pasteArea.activeFocus ? root.theme.primary : root.theme.outline

                            ScrollView {
                                anchors.fill: parent
                                anchors.margins: 8

                                TextArea {
                                    id: pasteArea
                                    wrapMode: TextArea.WrapAnywhere
                                    selectByMouse: true
                                    font.pixelSize: 12
                                    color: root.theme.inkSurface
                                    placeholderText: root.tr("ytm.cookiePlaceholder")
                                    placeholderTextColor: root.theme.inkSurfaceVariant
                                    background: null
                                }
                            }
                        }

                        RowLayout {
                            Layout.fillWidth: true
                            spacing: 8
                            visible: root.accountOut

                            Item { Layout.fillWidth: true }
                            MButton {
                                kind: "text"
                                text: root.pasteOpen ? root.tr("action.useCookie") : root.tr("action.pasteCookie")
                                enabled: !root.vm.accountBusy && (!root.pasteOpen || pasteArea.text.length > 0)
                                onClicked: {
                                    if (!root.pasteOpen) {
                                        root.pasteOpen = true
                                    } else {
                                        root.vm.useCookie(pasteArea.text)
                                        pasteArea.text = ""
                                        root.pasteOpen = false
                                    }
                                }
                            }
                            MButton {
                                text: root.tr("action.import")
                                enabled: !root.vm.accountBusy
                                onClicked: root.vm.importBrowserSession(root.browser)
                            }
                        }

                        RowLayout {
                            Layout.fillWidth: true
                            visible: root.accountIn

                            Item { Layout.fillWidth: true }
                            MButton {
                                kind: "outlined"
                                text: root.tr("action.signOut")
                                onClicked: root.vm.signOutAccount()
                            }
                        }
                    }

                    Card {
                        Heading { text: "Last.fm" }
                        Text {
                            Layout.fillWidth: true
                            text: root.vm.scrobbleStatus
                            color: root.theme.inkSurfaceVariant
                            font.pixelSize: 14
                            wrapMode: Text.Wrap
                        }

                        LastFmKeys {
                            Layout.fillWidth: true
                            visible: root.vm.scrobbleState === "unavailable"
                            vm: root.vm
                            theme: root.theme
                        }

                        TextEdit {
                            Layout.fillWidth: true
                            visible: root.vm.scrobbleState === "authorizing"
                            text: root.vm.scrobbleAuthUrl
                            readOnly: true
                            selectByMouse: true
                            wrapMode: TextEdit.WrapAnywhere
                            color: root.theme.inkSurfaceVariant
                            font.pixelSize: 12
                        }

                        MSwitch {
                            id: sendSwitch
                            Layout.fillWidth: true
                            visible: root.vm.scrobbleState === "signedIn"
                            text: root.tr("lastfm.send")
                            onToggled: root.vm.enableScrobbling(checked)
                            Binding { target: sendSwitch; property: "checked"; value: root.vm.scrobbleEnabled }
                        }

                        RowLayout {
                            Layout.fillWidth: true
                            spacing: 8

                            Item { Layout.fillWidth: true }
                            MButton {
                                visible: root.vm.scrobbleState === "authorizing"
                                kind: "text"
                                text: root.tr("action.cancel")
                                enabled: !root.vm.scrobbleBusy
                                onClicked: root.vm.cancelScrobblerSignIn()
                            }
                            MButton {
                                visible: root.vm.scrobbleState === "authorizing"
                                text: root.tr("action.continue")
                                enabled: !root.vm.scrobbleBusy
                                onClicked: root.vm.finishScrobblerSignIn()
                            }
                            MButton {
                                visible: root.vm.scrobbleState === "signedOut" || root.vm.scrobbleState === "reconnect"
                                text: root.tr("action.connect")
                                enabled: !root.vm.scrobbleBusy
                                onClicked: root.vm.connectScrobbler()
                            }
                            MButton {
                                visible: root.vm.scrobbleState === "signedIn" && root.vm.scrobblePending > 0
                                kind: "tonal"
                                text: root.tr("action.retry")
                                onClicked: root.vm.retryScrobbles()
                            }
                            MButton {
                                visible: root.vm.scrobbleState === "signedIn"
                                kind: "outlined"
                                text: root.tr("action.disconnect")
                                onClicked: root.vm.disconnectScrobbler()
                            }
                        }
                    }
                }
            }
        }
    }
}
