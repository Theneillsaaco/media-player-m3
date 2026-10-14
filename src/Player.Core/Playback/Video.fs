namespace Player.Core.Playback

open Player.Core.Contracts

/// What a track's video side offers.
type VideoPlan = {
    /// There is a video to show for this track.
    Available: bool
    /// The video to stream (the track itself, or its counterpart).
    VideoId: string
    /// Shown without the user asking, when the automatic setting is on: only official music videos.
    Automatic: bool
    /// The sound has to come from the video's own recording too: the track is the studio audio and the
    /// video is another upload, so the two would not line up otherwise.
    SwapAudio: bool
}

/// The user's choice for the current track: nothing yet, or video forced on or off.
type VideoChoice =
    | Unset = 0
    | Show = 1
    | Hide = 2
    
module Video =
    let private tail (videoType: string) =
        if isNull videoType then "" else videoType.Trim().ToUpperInvariant()
        
    let isOfficial (videoType: string) = (tail videoType).EndsWith "OMV"
    let isStudio (videoType: string) = (tail videoType).EndsWith "ATV"
    let isUpload (videoType: string) = (tail videoType).EndsWith "UGC"

    let none = { Available = false; VideoId = ""; Automatic = false; SwapAudio = false }

    /// An official music video plays its own audio and shows by default; an upload can be shown on request;
    /// studio audio with a video counterpart can swap to it on request.
    let plan (info: VideoInfo) : VideoPlan =
        let counterpartIsVideo =
            info.CounterpartId <> "" && (isOfficial info.CounterpartType || isUpload info.CounterpartType)
            
        if info.TrackId = "" then
            none
        elif isOfficial info.Type then
            { Available = true; VideoId = info.TrackId; Automatic = true; SwapAudio = false }
        elif isUpload info.Type then
            { Available = true; VideoId = info.TrackId; Automatic = false; SwapAudio = false }
        elif isStudio info.Type && counterpartIsVideo then
            { Available = true; VideoId = info.CounterpartId; Automatic = false; SwapAudio = true }
        else
            none
    
    /// Whether the video replaces the cover: the user's choice wins; without one, an automatic video shows
    /// when the setting is on.
    let showing (plan: VideoPlan) (automatic: bool) (choice: VideoChoice) =
        plan.Available
        && (match choice with
            | VideoChoice.Show -> true
            | VideoChoice.Hide -> false
            | _ -> plan.Automatic && automatic)
        
    /// The id the sound should come from instead of the track's own, or "" for no change.
    let audioFrom (plan: VideoPlan) (choice: VideoChoice) =
        if plan.Available && plan.SwapAudio && choice = VideoChoice.Show then plan.VideoId else ""
        
        /// A stream is usable until a margin before it expires.
    let isFresh (now: int64) (stream: VideoStream) =
        stream.ExpiresAt - Prefetch.margin > now

    