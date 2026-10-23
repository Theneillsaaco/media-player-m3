import QtQuick

// The one video of the app. It is a single item that Main.qml moves into whichever slot is on screen (the small
// slot of the playback bar, or the stage of the player page), so changing size never restarts the stream.
// QtMultimedia is only touched when a video is to be shown; a Qt without it only loses the video.
// vm is the VideoViewModel, playback the PlaybackViewModel.
Item {
    id: root

    property var vm: null
    property var playback: null
    /// Letterbox instead of cropping (the maximized player); the small slots crop to fill.
    property bool fit: false

    readonly property bool ready: loader.item !== null && loader.item.ready
    readonly property bool failed: loader.status === Loader.Error
    /// The picture waits for data (the frozen frame is on show): the panel draws a loading bar.
    readonly property bool waiting: loader.item !== null && loader.item.waiting

    visible: vm !== null && vm.showing

    Loader {
        id: loader
        anchors.fill: parent
        active: root.vm !== null && root.vm.showing && root.vm.url !== ""
        source: "VideoSurface.qml"
        opacity: root.ready ? 1 : 0
        Behavior on opacity { NumberAnimation { duration: 300 } }
        onLoaded: {
            item.vm = root.vm
            item.playback = root.playback
            item.fit = Qt.binding(() => root.fit)
        }
    }
}
