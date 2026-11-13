import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic
import "../Components"

// An album, a playlist or a mix: a header with a color wash taken from the theme, the cover with a soft shadow,
// the kind in small capitals, the title, who made it (a link to the artist when there is one), the Reproducir /
// Aleatorio buttons and its tracks. Scrolling past the header brings in a compact bar (cover, title, play and
// shuffle) one piece after the other. nav is the NavigationViewModel; Back is in the top bar.
Item {
    id: root

    required property var nav
    readonly property var theme: Window.window ? Window.window.theme : null
    readonly property var i18n: Window.window ? Window.window.i18n : null

    readonly property bool hasHeader: root.nav.collectionTitle !== ""
    readonly property color base: theme ? theme.surfaceContainer : "#F2ECF4"
    readonly property color wash: theme ? Qt.tint(theme.surfaceContainer, Qt.alpha(theme.primaryContainer, 0.6)) : "#E8DEF8"

    // 0 while the header is in view, 1 once it has scrolled away. The compact bar's parts follow it in turn.
    readonly property real scrolled: list.contentY - list.originY
    readonly property real shown: hasHeader ? Math.min(Math.max((scrolled - 150) / 60, 0), 1) : 0
    readonly property real artIn: stage(0.0)
    readonly property real titleIn: stage(0.12)
    readonly property real actionsIn: stage(0.3)

    function tr(key) { return i18n ? i18n.t(key) : key }

    function stage(from) { return Math.min(Math.max((shown - from) / 0.35, 0), 1) }

    ListView {
        id: list
        anchors.fill: parent
        clip: true
        bottomMargin: 12
        boundsBehavior: Flickable.StopAtBounds
        model: root.nav.collection
        ScrollBar.vertical: MScrollBar {}

        // The rows come in one after the other when the list is filled.
        populate: Transition {
            SequentialAnimation {
                PropertyAction { property: "opacity"; value: 0 }
                PauseAnimation { duration: Math.min(ViewTransition.index, 10) * 28 }
                ParallelAnimation {
                    Anim { property: "opacity"; to: 1; type: Anim.DefaultEffects }
                    Anim { property: "y"; from: ViewTransition.destination.y + 14; type: Anim.DefaultSpatial }
                }
            }
        }

        header: Item {
            width: list.width
            height: root.hasHeader ? 248 : 0
            visible: height > 0

            Rectangle { // the wash: the primary container fading into the page
                anchors.fill: parent
                gradient: Gradient {
                    GradientStop { position: 0.0; color: root.wash }
                    GradientStop { position: 1.0; color: root.base }
                }
            }

            RowLayout {
                anchors.fill: parent
                anchors.margins: 24
                anchors.topMargin: 28
                spacing: 24

                opacity: root.hasHeader ? 1 : 0
                Behavior on opacity { Anim { type: Anim.DefaultEffects } }
                transform: Translate {
                    y: root.hasHeader ? 0 : 14
                    Behavior on y { Anim { type: Anim.DefaultSpatial } }
                }

                Item {
                    Layout.preferredWidth: 184
                    Layout.preferredHeight: 184
                    Layout.alignment: Qt.AlignVCenter

                    // A soft shadow made of two translucent layers
                    Rectangle { x: -2; y: 6; width: parent.width + 4; height: parent.height; radius: 24; color: "#000000"; opacity: 0.10 }
                    Rectangle { x: 4; y: 12; width: parent.width - 8; height: parent.height; radius: 24; color: "#000000"; opacity: 0.08 }

                    Cover {
                        anchors.fill: parent
                        radius: 20
                        source: root.nav.collectionArt
                        pixels: 368
                    }
                }

                ColumnLayout {
                    id: details
                    Layout.fillWidth: true
                    Layout.alignment: Qt.AlignVCenter
                    spacing: 6

                    Text {
                        text: root.tr(root.nav.page === "album" ? "hero.album" : "hero.playlist").toUpperCase()
                        color: root.theme ? root.theme.primary : "#6750A4"
                        font.pixelSize: 12
                        font.weight: Font.Medium
                        font.letterSpacing: 1.4
                    }

                    Text {
                        Layout.fillWidth: true
                        text: root.nav.collectionTitle
                        color: root.theme ? root.theme.inkSurface : "#1D1B20"
                        font.pixelSize: 30
                        font.weight: Font.Medium
                        wrapMode: Text.Wrap
                        maximumLineCount: 2
                        elide: Text.ElideRight
                    }

                    Text {
                        Layout.alignment: Qt.AlignLeft
                        Layout.maximumWidth: details.width
                        text: root.nav.collectionSubtitle
                        color: subLink.containsMouse && root.nav.canOpenCollectionArtist
                            ? (root.theme ? root.theme.primary : "#6750A4")
                            : (root.theme ? root.theme.inkSurfaceVariant : "#49454F")
                        font.pixelSize: 14
                        font.underline: subLink.containsMouse && root.nav.canOpenCollectionArtist
                        elide: Text.ElideRight

                        MouseArea {
                            id: subLink
                            anchors.fill: parent
                            enabled: root.nav.canOpenCollectionArtist
                            hoverEnabled: true
                            cursorShape: enabled ? Qt.PointingHandCursor : Qt.ArrowCursor
                            onClicked: root.nav.openCollectionArtist()
                        }
                    }

                    RowLayout {
                        Layout.topMargin: 8
                        spacing: 8

                        MButton {
                            text: root.tr("action.play")
                            enabled: root.nav.collectionCount > 0
                            onClicked: root.nav.playCollection(false)
                        }
                        MButton {
                            kind: "tonal"
                            text: root.tr("action.shuffle")
                            enabled: root.nav.collectionCount > 0
                            onClicked: root.nav.playCollection(true)
                        }
                    }
                }
            }
        }

        delegate: TrackItem {
            required property int index
            required property var model
            width: ListView.view.width
            heading: model.title
            supporting: model.artist
            trailing: model.duration
            artUrl: model.artUrl
            videoId: model.videoId
            showAdd: true
            onActivated: root.nav.playCollectionItem(index)
            onAddRequested: root.nav.addCollectionItem(index)
            onPlayNextRequested: root.nav.playNextCollectionItem(index)
        }
    }

    // The compact bar: cover, then title, then the buttons, each one a little after the other.
    Rectangle {
        id: compact
        anchors.left: parent.left
        anchors.right: parent.right
        anchors.top: parent.top
        height: 56
        z: 5
        visible: root.shown > 0.01
        opacity: root.shown
        color: Qt.alpha(root.base, 0.96)

        Rectangle {
            anchors.left: parent.left
            anchors.right: parent.right
            anchors.bottom: parent.bottom
            height: 1
            color: root.theme ? root.theme.outlineVariant : "#CAC4CF"
            opacity: 0.6
        }

        RowLayout {
            anchors.fill: parent
            anchors.leftMargin: 16
            anchors.rightMargin: 12
            spacing: 12

            Cover {
                Layout.preferredWidth: 36
                Layout.preferredHeight: 36
                radius: 12
                source: root.nav.collectionArt
                opacity: root.artIn
                scale: 0.8 + 0.2 * root.artIn
            }

            Text {
                Layout.fillWidth: true
                text: root.nav.collectionTitle
                color: root.theme ? root.theme.inkSurface : "#1D1B20"
                font.pixelSize: 16
                font.weight: Font.Medium
                elide: Text.ElideRight
                opacity: root.titleIn
                transform: Translate { y: (1 - root.titleIn) * 8 }
            }

            RowLayout {
                spacing: 4
                opacity: root.actionsIn
                transform: Translate { y: (1 - root.actionsIn) * 8 }

                MIconButton {
                    glyph: "shuffle"
                    tip: root.tr("action.shuffle")
                    enabled: root.nav.collectionCount > 0
                    onClicked: root.nav.playCollection(true)
                }
                MIconButton {
                    glyph: "play"
                    tip: root.tr("action.play")
                    filled: true
                    enabled: root.nav.collectionCount > 0
                    onClicked: root.nav.playCollection(false)
                }
            }
        }
    }

    ColumnLayout {
        anchors.centerIn: parent
        width: parent.width - 48
        visible: list.count === 0 && root.nav.status !== ""
        spacing: 16

        LoadingIndicator {
            Layout.alignment: Qt.AlignHCenter
            visible: root.nav.working && !root.nav.failed
        }

        Text {
            Layout.fillWidth: true
            horizontalAlignment: Text.AlignHCenter
            wrapMode: Text.Wrap
            text: root.nav.status
            color: root.theme ? root.theme.inkSurfaceVariant : "#49454F"
            font.pixelSize: 14
        }
        MButton {
            Layout.alignment: Qt.AlignHCenter
            visible: root.nav.failed
            kind: "tonal"
            text: root.tr("action.retry")
            onClicked: root.nav.retry()
        }
    }
}
