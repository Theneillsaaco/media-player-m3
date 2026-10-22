import QtQuick
import QtQuick.Layouts
import QtQuick.Controls.Basic
import "../QML/Components"
import "../QML/Pages"
import "../QML/Dialogs"

ApplicationWindow {
    id: win
    width: 1100
    height: 700
    minimumWidth: 720
    minimumHeight: 480
    visible: true
    title: "Player"
    color: appTheme.surface

    // The Basic style's own pieces (scroll bars, text cursors, selections) read the palette, which is light
    // unless told otherwise: that is what made the dark mode look wrong.
    palette.window: appTheme.surface
    palette.windowText: appTheme.inkSurface
    palette.base: appTheme.surfaceContainer
    palette.text: appTheme.inkSurface
    palette.button: appTheme.surfaceContainerHigh
    palette.buttonText: appTheme.inkSurface
    palette.highlight: appTheme.primary
    palette.highlightedText: appTheme.inkPrimary
    palette.light: appTheme.surfaceContainerHighest
    palette.mid: appTheme.outlineVariant
    palette.dark: appTheme.outline
    palette.placeholderText: appTheme.inkSurfaceVariant

    // Leaf components find the theme and the texts through the window: Window.window.theme, Window.window.i18n.
    readonly property var theme: appTheme
    readonly property var i18n: appTr

    function tr(key) { return appTr.t(key) }

    // A short note in the snackbar.
    function toast(text) { navVm.notify(text) }

    // Likes from the lists (the ⋮ menu and the right click of a song). The ids come as a JSON array.
    readonly property var likedIds: {
        const map = ({})
        try { for (const id of JSON.parse(libraryVm.likedJson)) map[id] = true } catch (e) { }
        return map
    }
    function isLiked(id) { return likedIds[id] === true }
    function canLikeId(id) { return libraryVm.accountState === "signedIn" && id !== "" && !/^(https?:\/\/|\/)/i.test(id) }
    function toggleLikeOf(id) { libraryVm.toggleLikeOf(id) }

    property bool queueOpen: true
    property bool lyricsOpen: false

    // The player: "mini" (the picture lives in the playback bar and the pages show), "page" (the player takes the
    // left area) or "full" (maximized: the playback bar floats over the picture).
    property string playerMode: "mini"
    readonly property bool playerOpen: playerMode !== "mini"

    // Width of the queue and lyrics side panels: they give way on narrow windows.
    readonly property int sideWidth: Math.round(Math.min(380, Math.max(280, width * 0.28)))

    // Under this width the queue and the lyrics go below the picture instead of beside it.
    readonly property bool compact: width < 1000

    // A text field has the keyboard: the single-key shortcuts (space) must not take it.
    readonly property bool typing: activeFocusItem !== null && activeFocusItem.selectByMouse !== undefined

    PlaybackViewModel { id: playback }
    QueueViewModel { id: queueVm }
    SearchViewModel { id: searchVm }
    LibraryViewModel { id: libraryVm }
    SettingsViewModel { id: settingsVm }
    ThemeViewModel { id: themeVm }
    LocaleViewModel { id: localeVm }
    LyricsViewModel { id: lyricsVm }
    VideoViewModel { id: videoVm }
    NavigationViewModel { id: navVm }
    HistoryViewModel { id: historyVm }
    Theme { id: appTheme; tokensJson: themeVm.tokensJson }
    Tr { id: appTr; json: localeVm.stringsJson }

    // The queue and lyrics panels are built once and only moved and faded. Creating and destroying them on every
    // click is what made the player crash after many quick clicks. compactHost, wideQueueHost and wideLyricsHost
    // are the places they live in (below the picture on a narrow window, on the right on a wide one).
    QueuePanel {
        id: queuePanel
        parent: win.compact ? compactHost : wideQueueHost
        anchors.fill: parent
        radius: 28
        color: appTheme.surfaceContainer
        clip: true
        vm: queueVm
        opacity: win.queueOpen ? 1 : 0
        visible: opacity > 0
        Behavior on opacity { Anim { type: Anim.FastEffects } }
    }

    LyricsPanel {
        id: lyricsPanel
        parent: win.compact ? compactHost : wideLyricsHost
        anchors.fill: parent
        radius: 28
        color: appTheme.surfaceContainer
        clip: true
        vm: lyricsVm
        opacity: win.lyricsOpen ? 1 : 0
        visible: opacity > 0
        Behavior on opacity { Anim { type: Anim.FastEffects } }
    }

    // The colors follow the cover that plays while the option is on (Settings > Appearance).
    CoverColor {
        id: coverColor
        source: playback.currentArt
        active: themeVm.dynamic
        onFound: (hex) => themeVm.useCoverColor(hex)
    }

    Component {
        id: queueComp
        QueuePanel {
            radius: 28
            color: appTheme.surfaceContainer
            clip: true
            vm: queueVm
        }
    }

    Component {
        id: lyricsComp
        LyricsPanel {
            radius: 28
            color: appTheme.surfaceContainer
            clip: true
            vm: lyricsVm
        }
    }

    SearchSuggestions {
        id: suggestions
        field: input
        entries: searchVm.suggestionsJson.length > 0 ? JSON.parse(searchVm.suggestionsJson) : []
        onChosen: (text) => {
            input.text = text
            close()
            searchVm.clearSuggestions()
            win.playerMode = "mini"
            navVm.goRoot("search")
            searchVm.search(text)
        }
    }

    Timer {
        id: suggestTimer
        interval: 220
        onTriggered: searchVm.suggest(input.text)
    }

    SettingsDialog { id: settings; vm: settingsVm; appearanceVm: themeVm; videoVm: videoVm; localeVm: localeVm; theme: appTheme }

    // Called by the lists and the playback bar: a name opens its page, from wherever the user is.
    function openEntity(kind, entityId) {
        playerMode = "mini"
        navVm.openEntity(kind, entityId)
    }
    function openArtistOf(videoId) {
        playerMode = "mini"
        navVm.openTrackArtist(videoId)
    }
    function openAlbumOf(videoId) {
        playerMode = "mini"
        navVm.openTrackAlbum(videoId)
    }

    function pollAll() {
        settingsVm.poll()
        searchVm.poll()
        libraryVm.poll()
        lyricsVm.poll()
        videoVm.poll()
        navVm.poll()
        playback.poll()
    }

    // Polls only while something is playing, searching or working in the background,
    // so an idle player wakes nothing. It ticks faster while the lyrics are open, to follow the line.
    Timer {
        interval: win.lyricsOpen ? 120 : 250
        repeat: true
        running: (playback.isLoaded && !playback.paused) || searchVm.searching || searchVm.suggesting || libraryVm.working || settingsVm.busy || lyricsVm.loading || videoVm.loading || navVm.working
        onTriggered: win.pollAll()
    }

    // Follows the desktop's light/dark setting, only while the mode is "system".
    Timer {
        interval: 1000
        repeat: true
        running: themeVm.mode === "system"
        onTriggered: themeVm.poll()
    }

    // Runs what the desktop asked for (playerctl, media keys). Cheap: it only looks at a queue.
    Timer {
        interval: 250
        repeat: true
        running: true
        onTriggered: playback.pump()
    }

    // The snackbar goes away by itself.
    Timer {
        interval: 4000
        running: navVm.message.length > 0
        onTriggered: navVm.dismissMessage()
    }

    // Opening the library, or signing in while it is open, loads the playlists.
    property string currentPage: navVm.page
    onCurrentPageChanged: {
        if (currentPage === "library") libraryVm.loadLibrary()
        else if (currentPage === "history") historyVm.load()
    }
    property string accountState: libraryVm.accountState
    onAccountStateChanged: if (currentPage === "library") libraryVm.loadLibrary()

    // The queue and the lyrics share the right-hand side: opening one closes the other.
    onQueueOpenChanged: if (queueOpen) lyricsOpen = false
    onLyricsOpenChanged: {
        lyricsVm.panelShown(lyricsOpen)
        if (lyricsOpen) queueOpen = false
    }

    // ---- keyboard ----

    // Escape leaves the maximized player.
    Shortcut {
        sequence: "Esc"
        enabled: win.playerMode === "full"
        onActivated: win.playerMode = "page"
    }
    Shortcut {
        sequence: "Alt+Left"
        onActivated: navVm.goBack()
    }
    Shortcut {
        sequence: "Space"
        enabled: !win.typing && !settings.visible
        onActivated: playback.toggle()
    }
    Shortcut {
        sequence: "Ctrl+Right"
        onActivated: playback.next()
    }
    Shortcut {
        sequence: "Ctrl+Left"
        onActivated: playback.previous()
    }
    Shortcut {
        sequence: "Ctrl+F"
        onActivated: { input.forceActiveFocus(); input.selectAll() }
    }
    Shortcut {
        sequence: "Ctrl+L"
        onActivated: win.queueOpen = !win.queueOpen
    }
    Shortcut {
        sequence: "Ctrl+Shift+L"
        onActivated: win.lyricsOpen = !win.lyricsOpen
    }

    ColumnLayout {
        anchors.fill: parent
        spacing: 0

        // Top bar: back, search and the panels
        RowLayout {
            Layout.fillWidth: true
            Layout.leftMargin: 12
            Layout.rightMargin: 12
            Layout.topMargin: 8
            Layout.bottomMargin: 4
            spacing: 8

            MIconButton {
                glyph: "back"
                tip: win.tr("tip.back")
                enabled: win.playerOpen || navVm.canBack
                onClicked: {
                    if (win.playerOpen) win.playerMode = "mini"
                    else navVm.goBack()
                }
            }
            MSearchField {
                id: input
                Layout.fillWidth: true
                placeholderText: win.tr("search.placeholder")
                clearTip: win.tr("tip.clearSearch")
                onTextEdited: suggestTimer.restart()
                Keys.onDownPressed: suggestions.step(1)
                Keys.onUpPressed: suggestions.step(-1)
                Keys.onEscapePressed: { suggestions.close(); searchVm.clearSuggestions() }
                onAccepted: {
                    const query = suggestions.pick() !== "" ? suggestions.pick() : text
                    suggestions.close()
                    searchVm.clearSuggestions()
                    if (query !== text) text = query
                    win.playerMode = "mini"
                    navVm.goRoot("search")
                    searchVm.search(query)
                }
            }
            MIconButton {
                glyph: "album"
                tip: win.tr("tip.player")
                selected: win.playerOpen
                onClicked: win.playerMode = win.playerOpen ? "mini" : "page"
            }
            MIconButton {
                glyph: "lyrics"
                tip: win.tr("tip.lyrics")
                selected: win.lyricsOpen
                onClicked: win.lyricsOpen = !win.lyricsOpen
            }
            MIconButton {
                glyph: "queue"
                tip: win.tr("tip.queue")
                selected: win.queueOpen
                onClicked: win.queueOpen = !win.queueOpen
            }
            MIconButton {
                glyph: "settings"
                tip: win.tr("tip.settings")
                onClicked: settings.open()
            }
        }

        RowLayout {
            Layout.fillWidth: true
            Layout.fillHeight: true
            Layout.leftMargin: 4
            Layout.rightMargin: 12
            spacing: 8

            NavRail {
                Layout.fillHeight: true
                Layout.preferredWidth: 80
                current: navVm.section
                onChosen: (key) => {
                    win.playerMode = "mini"
                    navVm.goRoot(key)
                }
            }

            ColumnLayout {
                Layout.fillWidth: true
                Layout.fillHeight: true
                spacing: 8

                // The pages and the player share one rounded card.
                Rectangle {
                    id: stageCard
                    Layout.fillWidth: true
                    Layout.fillHeight: true
                    radius: win.playerMode === "full" ? 0 : 28
                    clip: true
                    color: win.playerMode === "full" ? "#000000" : appTheme.surfaceContainer

                    // Pages cross-fade (Material "fade through": the old one out, then the new one in).
                    FadePage {
                        shown: !win.playerOpen && navVm.page === "search"
                        SearchPage {
                            anchors.fill: parent
                            vm: searchVm
                            onEntityActivated: (kind, entityId) => win.openEntity(kind, entityId)
                        }
                    }

                    FadePage {
                        shown: !win.playerOpen && navVm.page === "home"
                        HomePage {
                            anchors.fill: parent
                            nav: navVm
                            onEntityActivated: (kind, entityId) => win.openEntity(kind, entityId)
                        }
                    }

                    FadePage {
                        shown: !win.playerOpen && navVm.page === "library"
                        LibraryPage {
                            anchors.fill: parent
                            vm: libraryVm
                            onSettingsRequested: settings.open()
                        }
                    }

                    FadePage {
                        shown: !win.playerOpen && navVm.page === "history"
                        HistoryPage {
                            anchors.fill: parent
                            vm: historyVm
                        }
                    }

                    FadePage {
                        shown: !win.playerOpen && (navVm.page === "album" || navVm.page === "collection")
                        CollectionPage {
                            anchors.fill: parent
                            nav: navVm
                        }
                    }

                    FadePage {
                        shown: !win.playerOpen && navVm.page === "artist"
                        ArtistPage {
                            anchors.fill: parent
                            nav: navVm
                            onEntityActivated: (kind, entityId) => win.openEntity(kind, entityId)
                        }
                    }

                    PlayerPage {
                        id: playerPage
                        anchors.fill: parent
                        visible: win.playerOpen
                        mode: win.playerMode
                        playback: playback
                        video: videoVm
                        theme: appTheme
                        videoReady: videoLayer.ready
                        videoUnavailable: videoLayer.failed
                        onMinimizeRequested: win.playerMode = "mini"
                        onMaximizeRequested: win.playerMode = "full"
                        onRestoreRequested: win.playerMode = "page"
                    }
                }

                // Narrow window: the queue or the lyrics go under the picture, as YouTube Music does.
                Item {
                    id: compactHost
                    Layout.fillWidth: true
                    Layout.preferredHeight: Math.max(180, Math.round(win.height * 0.38))
                    visible: win.compact && win.playerMode !== "full" && (win.queueOpen || win.lyricsOpen)
                }
            }

            // Wide window: they stay on the right.
            Item {
                id: wideQueueHost
                Layout.preferredWidth: win.sideWidth
                Layout.fillHeight: true
                visible: !win.compact && win.queueOpen
            }

            Item {
                id: wideLyricsHost
                Layout.preferredWidth: win.sideWidth + 20
                Layout.fillHeight: true
                visible: !win.compact && win.lyricsOpen
            }
        }

        // Where the playback bar sits in the window (it moves onto the video in the maximized player).
        Item {
            id: barHost
            Layout.fillWidth: true
            Layout.leftMargin: 12
            Layout.rightMargin: 12
            Layout.topMargin: 8
            Layout.bottomMargin: 12
            Layout.preferredHeight: 96
            visible: win.playerMode !== "full"
        }
    }

    PlaybackBar {
        id: bar
        parent: win.playerMode === "full" ? playerPage.dock : barHost
        anchors.fill: parent
        playback: playback
        queue: queueVm
        library: libraryVm
        video: videoVm
        theme: appTheme
        docked: win.playerMode === "full"
        videoInSlot: win.playerMode === "mini" && videoVm.showing
        onCoverClicked: win.playerMode = win.playerOpen ? "mini" : "page"
    }

    // The one video of the app, moved into whichever slot is on screen so it never restarts.
    VideoLayer {
        id: videoLayer
        parent: win.playerOpen ? playerPage.stage : bar.miniSlot
        anchors.fill: parent
        vm: videoVm
        playback: playback
        fit: win.playerMode === "full"
    }

    // Snackbar: short messages ("added to the queue", "this track has no album"...).
    Rectangle {
        id: snack
        anchors.horizontalCenter: parent.horizontalCenter
        anchors.bottom: parent.bottom
        anchors.bottomMargin: navVm.message.length > 0 ? 124 : 108
        z: 100
        width: Math.min(snackText.implicitWidth + 40, parent.width - 48)
        height: 48
        radius: 12
        color: appTheme.inverseSurface
        opacity: navVm.message.length > 0 ? 1 : 0
        visible: opacity > 0
        Behavior on opacity { NumberAnimation { duration: appTheme.durationShort3; easing.type: Easing.BezierSpline; easing.bezierCurve: appTheme.easingStandard } }
        Behavior on anchors.bottomMargin { NumberAnimation { duration: appTheme.durationMedium1; easing.type: Easing.BezierSpline; easing.bezierCurve: appTheme.easingEmphasizedDecelerate } }

        Text {
            id: snackText
            anchors.fill: parent
            anchors.leftMargin: 20
            anchors.rightMargin: 20
            verticalAlignment: Text.AlignVCenter
            text: navVm.message
            color: appTheme.inverseOnSurface
            font.pixelSize: 14
            elide: Text.ElideRight
        }
    }
}
