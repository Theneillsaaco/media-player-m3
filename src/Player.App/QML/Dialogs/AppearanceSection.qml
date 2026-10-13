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

    readonly property var accents: ["#6750a4", "#386a20", "#00658e", "#8f4c38", "#7d5260", "#6b5f00"]

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

    Heading { text: "Apariencia" }

    Choices {
        Repeater {
            model: [
                { key: "system", label: "Sistema" },
                { key: "light", label: "Claro" },
                { key: "dark", label: "Oscuro" }
            ]
            MChip {
                required property var modelData
                text: modelData.label
                selected: section.vm.mode === modelData.key
                onClicked: section.vm.chooseMode(modelData.key)
            }
        }
    }

    Text {
        text: "Color"
        color: section.theme.onSurfaceVariant
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
                border.color: chosen ? section.theme.onSurface : section.theme.outlineVariant

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
            placeholderText: "Otro color: #rrggbb"
            placeholderTextColor: section.theme.onSurfaceVariant
            color: section.theme.onSurface
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
            text: "Aplicar"
            enabled: hexField.text.length >= 6
            onClicked: section.vm.chooseAccent(hexField.text)
        }
    }

    Text {
        text: "Variante"
        color: section.theme.onSurfaceVariant
        font.pixelSize: 12
    }

    Choices {
        Repeater {
            model: [
                { key: "tonalSpot", label: "Tonal" },
                { key: "neutral", label: "Neutral" },
                { key: "vibrant", label: "Vibrante" },
                { key: "expressive", label: "Expresiva" },
                { key: "content", label: "Contenido" }
            ]
            MChip {
                required property var modelData
                text: modelData.label
                selected: section.vm.variant === modelData.key
                onClicked: section.vm.chooseVariant(modelData.key)
            }
        }
    }

    Text {
        text: "Contraste"
        color: section.theme.onSurfaceVariant
        font.pixelSize: 12
    }

    Choices {
        Repeater {
            model: [
                { key: "standard", label: "Estándar" },
                { key: "medium", label: "Medio" },
                { key: "high", label: "Alto" }
            ]
            MChip {
                required property var modelData
                text: modelData.label
                selected: section.vm.contrast === modelData.key
                onClicked: section.vm.chooseContrast(modelData.key)
            }
        }
    }
}
