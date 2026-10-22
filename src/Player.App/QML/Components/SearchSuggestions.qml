import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic

// The suggestions under the search bar. field is the MSearchField, entries the list of texts. It never takes the
// keyboard: the field drives it (step() with the arrows, pick() on Enter) and chosen(text) fires on a click.
Popup {
    id: root

    required property var field
    property var entries: []
    property int current: -1

    readonly property var theme: field && field.Window.window ? field.Window.window.theme : null

    signal chosen(string text)

    // Moves the highlight; -1 (nothing) is one of the stops, so the arrows can go back to what was typed.
    function step(delta) {
        if (entries.length === 0)
            return
        if (!opened)
            open()
        const stops = entries.length + 1
        current = (((current + 1 + delta) % stops) + stops) % stops - 1
    }

    function pick() {
        return opened && current >= 0 && current < entries.length ? entries[current] : ""
    }

    function refresh() {
        if (field.activeFocus && entries.length > 0)
            open()
        else
            close()
    }

    onEntriesChanged: {
        current = -1
        refresh()
    }

    Connections {
        target: root.field
        function onActiveFocusChanged() { root.refresh() }
        function onTextChanged() { if (root.field.text.length === 0) root.close() }
    }

    parent: field
    y: field.height + 4
    width: field.width
    padding: 8
    focus: false
    closePolicy: Popup.NoAutoClose

    enter: Transition { Anim { property: "opacity"; from: 0; to: 1; type: Anim.FastEffects } }
    exit: Transition { Anim { property: "opacity"; from: 1; to: 0; type: Anim.FastEffects } }

    background: Rectangle {
        radius: 28
        color: root.theme ? root.theme.surfaceContainerHigh : "#ECE6F0"
        border.width: 1
        border.color: root.theme ? root.theme.outlineVariant : "#CAC4CF"
    }

    contentItem: ListView {
        implicitHeight: Math.min(contentHeight, 8 * 48)
        clip: true
        boundsBehavior: Flickable.StopAtBounds
        interactive: contentHeight > height
        model: root.entries

        delegate: Item {
            id: entry

            required property int index
            required property string modelData

            width: ListView.view.width
            height: 48

            Rectangle {
                anchors.fill: parent
                radius: 24
                color: root.theme ? root.theme.inkSurface : "#1D1B20"
                opacity: entry.index === root.current ? 0.12 : area.containsMouse ? 0.08 : 0
            }

            RowLayout {
                anchors.fill: parent
                anchors.leftMargin: 16
                anchors.rightMargin: 16
                spacing: 16

                Icon {
                    name: "search"
                    size: 20
                    tint: root.theme ? root.theme.inkSurfaceVariant : "#49454F"
                }
                Text {
                    Layout.fillWidth: true
                    text: entry.modelData
                    color: root.theme ? root.theme.inkSurface : "#1D1B20"
                    font.pixelSize: 16
                    elide: Text.ElideRight
                }
            }

            MouseArea {
                id: area
                anchors.fill: parent
                hoverEnabled: true
                cursorShape: Qt.PointingHandCursor
                onEntered: root.current = entry.index
                onClicked: root.chosen(entry.modelData)
            }
        }
    }
}
