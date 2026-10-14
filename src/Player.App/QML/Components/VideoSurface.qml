import QtQuick
import QtMultimedia

// The picture of a music video. mpv keeps playing the sound and stays the clock: this player has no audio output
// and only follows mpv's position, seeking when it drifts far and nudging its speed when it drifts a little.
// It is loaded on demand by NowPlayingPanel (Loader), so a Qt without QtMultimedia only loses the video.
// vm is the VideoViewModel, playback the PlaybackViewModel; the panel assigns both right after loading.
Item {
    id: root

    property var vm: null
    property var playback: null

    /// True once the first frames are playing, so the panel can fade from the cover to the video.
    readonly property bool ready: player.playbackState === MediaPlayer.PlayingState && player.hasVideo && player.position > 0

    // mpv's position is read a few times a second; between readings the clock is extrapolated.
    property real anchor: 0
    property double anchorAt: Date.now()

    function wanted() { // milliseconds
        const p = root.playback
        if (!p) return 0
        return (root.anchor + (p.paused ? 0 : (Date.now() - root.anchorAt) / 1000)) * 1000
    }

    function align(hard) {
        if (!root.playback) return
        const want = root.wanted()

        if (root.playback.paused) {
            if (player.playbackState === MediaPlayer.PlayingState) player.pause()
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

    Connections {
        target: root.playback
        function onPositionChanged() {
            root.anchor = root.playback.position
            root.anchorAt = Date.now()
        }
        function onPausedChanged() {
            root.anchor = root.playback.position
            root.anchorAt = Date.now()
            root.align(true)
        }
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
        fillMode: VideoOutput.PreserveAspectCrop
    }

    // Only while it plays: a paused player needs no wake-ups.
    Timer {
        interval: 250
        repeat: true
        running: root.playback !== null && !root.playback.paused
        onTriggered: root.align(false)
    }

    Component.onCompleted: {
        if (root.playback) {
            root.anchor = root.playback.position
            root.anchorAt = Date.now()
        }
        align(true)
    }
}
