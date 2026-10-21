import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic
import "../Components"

// Settings > Apariencia: light/dark/system, accent color, scheme variant and contrast.
// vm is the ThemeViewModel; theme is the window's Theme.
ColumnLayout {
    id: section

    required property var vm
    required property var theme
    readonly property var i18n: Window.window ? Window.window.i18n : null

    readonly property var accents: ["#6750a4", "#386a20", "#00658e", "#8f4c38", "#7d5260", "#6b5f00"]

    function tr(key) { return i18n ? i18n.t(key) : key }

    spacing: 12

    component Heading: Text {
        color: section.theme.primary
        font.pixelSize: 14
        font.weight: Font.Medium
    }

    component Choices: Flow {
        Layout.fillWidth: true
        spacing: 8
    }

    Heading { text: section.tr("appearance.title") }

    Choices {
        Repeater {
            model: [
                { key: "system", label: "mode.system" },
                { key: "light", label: "mode.light" },
                { key: "dark", label: "mode.dark" }
            ]
            MChip {
                required property var modelData
                text: section.tr(modelData.label)
                selected: section.vm.mode === modelData.key
                onClicked: section.vm.chooseMode(modelData.key)
            }
        }
    }

    MSwitch {
        id: dynamicSwitch
        Layout.fillWidth: true
        text: section.tr("appearance.dynamic")
        onToggled: section.vm.chooseDynamic(checked)

        // A Binding keeps following the model after the user has toggled the switch.
        Binding {
            target: dynamicSwitch
            property: "checked"
            value: section.vm.dynamic
        }
    }

    Text {
        text: section.tr("appearance.color")
        color: section.theme.inkSurfaceVariant
        font.pixelSize: 12
    }

    Choices {
        spacing: 12

        Repeater {
            model: section.accents
            Rectangle {
                id: swatch
                required property string modelData
                readonly property bool chosen: section.vm.accent === modelData
                readonly property color seed: Qt.color(modelData)
                width: 40
                height: 40
                radius: 20
                color: seed
                border.width: chosen ? 3 : 1
                border.color: chosen ? section.theme.inkSurface : section.theme.outlineVariant

                Icon {
                    anchors.centerIn: parent
                    visible: swatch.chosen
                    name: "check"
                    size: 20
                    tint: (0.2126 * swatch.seed.r + 0.7152 * swatch.seed.g + 0.0722 * swatch.seed.b) > 0.5 ? "#000000" : "#ffffff"
                }
                MouseArea {
                    anchors.fill: parent
                    onClicked: section.vm.chooseAccent(swatch.modelData)
                }
            }
        }
    }

    RowLayout {
        Layout.fillWidth: true
        spacing: 8

        TextField {
            id: hexField
            Layout.fillWidth: true
            implicitHeight: 40
            placeholderText: section.tr("appearance.otherColor")
            placeholderTextColor: section.theme.inkSurfaceVariant
            color: section.theme.inkSurface
            font.pixelSize: 14
            maximumLength: 7
            selectByMouse: true
            leftPadding: 16
            rightPadding: 16
            verticalAlignment: TextInput.AlignVCenter
            background: Rectangle {
                radius: 8
                color: "transparent"
                border.width: hexField.activeFocus ? 2 : 1
                border.color: hexField.activeFocus ? section.theme.primary : section.theme.outline
            }
            onAccepted: { section.vm.chooseAccent(text); focus = false }
        }
        MButton {
            kind: "tonal"
            text: section.tr("action.apply")
            enabled: hexField.text.length >= 6
            onClicked: section.vm.chooseAccent(hexField.text)
        }
    }

    Text {
        text: section.tr("appearance.variant")
        color: section.theme.inkSurfaceVariant
        font.pixelSize: 12
    }

    Choices {
        Repeater {
            model: [
                { key: "tonalSpot", label: "variant.tonalSpot" },
                { key: "neutral", label: "variant.neutral" },
                { key: "vibrant", label: "variant.vibrant" },
                { key: "expressive", label: "variant.expressive" },
                { key: "content", label: "variant.content" }
            ]
            MChip {
                required property var modelData
                text: section.tr(modelData.label)
                selected: section.vm.variant === modelData.key
                onClicked: section.vm.chooseVariant(modelData.key)
            }
        }
    }

    Text {
        text: section.tr("appearance.contrast")
        color: section.theme.inkSurfaceVariant
        font.pixelSize: 12
    }

    Choices {
        Repeater {
            model: [
                { key: "standard", label: "contrast.standard" },
                { key: "medium", label: "contrast.medium" },
                { key: "high", label: "contrast.high" }
            ]
            MChip {
                required property var modelData
                text: section.tr(modelData.label)
                selected: section.vm.contrast === modelData.key
                onClicked: section.vm.chooseContrast(modelData.key)
            }
        }
    }
}
