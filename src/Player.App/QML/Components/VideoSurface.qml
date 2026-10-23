import QtQuick
import QtMultimedia

// The picture of a music video. mpv keeps playing the sound and stays the clock: this player has no audio output
// and only follows mpv's position, seeking when it drifts far and nudging its speed when it drifts a little.
//
// The picture only runs while the sound really does: a track is loaded, not paused and not waiting for data
// (playback.audioWaiting). While the sound loads or refills its cache the picture holds still, so it can never run
// ahead of the music. The other way round, when the picture has to wait for data while the sound goes on, it
// shows the last frame frozen and `waiting` turns on, so the panel can show a loading bar over it.
//
// The frozen frame: a snapshot of the picture is taken when it stops or is about to be moved by a seek, and it is
// drawn over the video until the first new frame arrives. It is not moved for a small drift, because every seek
// makes the stream ask the server again, which is what made a pause longer than a few seconds look like the video
// had been thrown away.
//
// It is loaded on demand by VideoLayer (Loader), so a Qt without QtMultimedia only loses the video.
// vm is the VideoViewModel, playback the PlaybackViewModel; the layer assigns both right after loading.
Item {
    id: root

    property bool fit: false
    property var vm: null
    property var playback: null

    /// The sound is playing right now.
    readonly property bool running: playback !== null && playback.isLoaded && !playback.paused && !playback.audioWaiting

    /// The picture has shown frames. It stays true while paused or waiting, so the cover does not come back over
    /// the frozen frame; it is false again when the stream changes.
    property bool ready: false

    /// The picture should be moving and is not (still loading, seeking, waiting for data), for long enough that a
    /// loading bar makes sense.
    property bool waiting: false

    property bool stalled: false       // playing, but the position has not moved for a while
    property bool awaitingFrame: true  // resumed or moved, and no new frame has arrived yet
    property bool captured: false      // the snapshot has a picture in it
    property double resumedAt: Date.now()
    property double lastChangeAt: Date.now()
    property real lastPos: 0

    readonly property bool waitingRaw: running && (!ready || stalled || awaitingFrame)

    // mpv's position is read a few times a second; between readings the clock is extrapolated.
    property real anchor: 0
    property double anchorAt: Date.now()

    function wanted() { // milliseconds
        const p = root.playback
        if (!p) return 0
        return (root.anchor + (root.running ? (Date.now() - root.anchorAt) / 1000 : 0)) * 1000
    }

    // Moves the picture to a position. The snapshot is taken first, from what is on screen now.
    function seekTo(ms) {
        if (!root.awaitingFrame && !root.stalled) {
            frozen.scheduleUpdate()
            root.captured = true
        }
        root.awaitingFrame = true
        root.resumedAt = Date.now()
        player.position = ms
    }

    function align(hard) {
        if (!root.playback) return
        const want = root.wanted()
        if (!isFinite(want)) return

        if (!root.running) {
            if (player.playbackState !== MediaPlayer.PausedState) player.pause()
            // A paused picture is only moved when the music was moved far from it (a seek while paused).
            if (Math.abs(player.position - want) > (hard ? 600 : 300)) player.position = want
            return
        }

        const drift = player.position - want // positive: the picture is ahead of the sound
        if (Math.abs(drift) > (hard ? 400 : 700)) {
            root.seekTo(want)
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

    // Looks at whether the picture really moves while it should.
    function watch() {
        const now = Date.now()
        const pos = player.position
        const playing = player.playbackState === MediaPlayer.PlayingState

        if (pos !== root.lastPos) {
            root.lastPos = pos
            root.lastChangeAt = now
        }
        root.stalled = playing && now - root.lastChangeAt > 800

        // If the frame signal never came, do not keep the snapshot up for ever.
        if (root.awaitingFrame && playing && now - root.resumedAt > 1500)
            root.awaitingFrame = false
    }

    onRunningChanged: {
        if (root.running) {
            root.awaitingFrame = true
            root.resumedAt = Date.now()
            root.lastChangeAt = Date.now()
            root.stalled = false
        } else if (!root.awaitingFrame && !root.stalled) {
            frozen.scheduleUpdate() // the last good picture, taken before anything can blank it
            root.captured = true
        }
    }

    onWaitingRawChanged: if (!waitingRaw) root.waiting = false

    readonly property bool playingFrames: player.playbackState === MediaPlayer.PlayingState && player.hasVideo && player.position > 0
    onPlayingFramesChanged: if (playingFrames) root.ready = true

    Timer {
        interval: 500
        running: root.waitingRaw
        onTriggered: root.waiting = true
    }

    Connections {
        target: root.playback
        function onPositionChanged() { root.reanchor() }
        function onPausedChanged() { root.reanchor(); root.align(true) }
        function onIsLoadedChanged() { root.reanchor(); root.align(true) }
        function onAudioWaitingChanged() { root.reanchor(); root.align(true) }
    }

    MediaPlayer {
        id: player
        source: root.vm ? root.vm.url : ""
        videoOutput: output
        onSourceChanged: {
            root.ready = false
            root.captured = false
            root.awaitingFrame = true
            root.stalled = false
        }
        onMediaStatusChanged: if (mediaStatus === MediaPlayer.LoadedMedia) root.align(true)
        onErrorOccurred: (error, errorString) => { if (root.vm) root.vm.streamFailed(errorString) }
    }

    VideoOutput {
        id: output
        anchors.fill: parent
        visible: root.width >= 8 && root.height >= 8
        fillMode: root.fit ? VideoOutput.PreserveAspectFit : VideoOutput.PreserveAspectCrop
    }

    // The frozen frame, over the video.
    ShaderEffectSource {
        id: frozen
        anchors.fill: output
        sourceItem: output
        live: false
        hideSource: false
        visible: root.captured && (root.awaitingFrame || !root.running)
    }

    // The first frame after a resume or a seek lifts the snapshot. Only listened to while it is waiting.
    Connections {
        target: output.videoSink
        enabled: root.awaitingFrame
        function onVideoFrameChanged(frame) {
            if (Date.now() - root.resumedAt > 30)
                root.awaitingFrame = false
        }
    }

    // Only while the sound plays: a still picture needs no wake-ups.
    Timer {
        interval: 250
        repeat: true
        running: root.running
        onTriggered: {
            root.watch()
            root.align(false)
        }
    }

    Component.onCompleted: {
        root.reanchor()
        align(true)
    }
}
