import QtQuick
import QtMultimedia

// The picture of a music video. mpv keeps playing the sound and stays the clock: this player has no audio output
// and only follows mpv's position, seeking when it drifts far and nudging its speed when it drifts a little.
// The picture only runs while the sound does (a track is loaded and not paused): when the music stops, ends or
// has not started, the video holds still. mpv's "pause" property reads "no" when it is idle, so it is not enough.
// It is loaded on demand by NowPlayingPanel (Loader), so a Qt without QtMultimedia only loses the video.
// vm is the VideoViewModel, playback the PlaybackViewModel; the panel assigns both right after loading.
Item {
    id: root

    property bool fit: false
    property var vm: null
    property var playback: null

    /// The sound is playing right now.
    readonly property bool running: playback !== null && playback.isLoaded && !playback.paused

    /// True once the first frames are playing, so the panel can fade from the cover to the video.
    readonly property bool ready: player.playbackState === MediaPlayer.PlayingState && player.hasVideo && player.position > 0

    // mpv's position is read a few times a second; between readings the clock is extrapolated.
    property real anchor: 0
    property double anchorAt: Date.now()

    function wanted() { // milliseconds
        const p = root.playback
        if (!p) return 0
        return (root.anchor + (root.running ? (Date.now() - root.anchorAt) / 1000 : 0)) * 1000
    }

    function align(hard) {
        if (!root.playback) return
        const want = root.wanted()
        if (!isFinite(want)) return

        if (!root.running) {
            if (player.playbackState !== MediaPlayer.PausedState) player.pause()
            if (hard || Math.abs(player.position - want) > 300) player.position = want
            return
        }

        const drift = player.position - want // positive: the picture is ahead of the sound
        if (hard || Math.abs(drift) > 700) {
            player.position = want
            player.playbackRate = 1.0
        } else if (Math.abs(drift) > 150) {
            player.playbackRate = drift > 0 ? 0.95 : 1.05
        } else {
            player.playbackRate = 1.0
        }
        if (player.playbackState !== MediaPlayer.PlayingState) player.play()
    }

    function reanchor() {
        if (!root.playback) return
        root.anchor = root.playback.position
        root.anchorAt = Date.now()
    }

    Connections {
        target: root.playback
        function onPositionChanged() { root.reanchor() }
        function onPausedChanged() { root.reanchor(); root.align(true) }
        function onIsLoadedChanged() { root.reanchor(); root.align(true) }
    }

    MediaPlayer {
        id: player
        source: root.vm ? root.vm.url : ""
        videoOutput: output
        onMediaStatusChanged: if (mediaStatus === MediaPlayer.LoadedMedia) root.align(true)
        onErrorOccurred: (error, errorString) => { if (root.vm) root.vm.streamFailed(errorString) }
    }

    VideoOutput {
        id: output
        anchors.fill: parent
        visible: root.width >= 8 && root.height >= 8
        fillMode: root.fit ? VideoOutput.PreserveAspectFit : VideoOutput.PreserveAspectCrop
    }

    // Only while the sound plays: a still picture needs no wake-ups.
    Timer {
        interval: 250
        repeat: true
        running: root.running
        onTriggered: root.align(false)
    }

    Component.onCompleted: {
        root.reanchor()
        align(true)
    }
}
