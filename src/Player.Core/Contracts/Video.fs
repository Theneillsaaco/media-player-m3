namespace Player.Core.Contracts

open System.Threading.Tasks

/// What YouTube Music says about the video side of a track. Types are the raw "MUSIC_VIDEO_TYPE_OMV" (official
/// music video), "..._ATV" (studio audio) or "..._UGC" (user upload) strings, or "" when unknown. The counterpart
/// is the other version of the same song, the one behind the song/video switch of YouTube Music.
type VideoInfo = {
    TrackId: string
    Type: string
    CounterpartId: string
    CounterpartType: string
}

/// A video-only stream. ExpiresAt is unix seconds, like the audio streams.
type VideoStream = {
    Url: string
    ExpiresAt: int64
    Width: int
    Height: int
}

/// Where videos come from. Methods run on any thread and fail the task on network trouble.
type IVideoSource =
    abstract GetVideoInfo: id: string -> Task<VideoInfo>
    abstract GetVideoStream: id: string -> Task<VideoStream>