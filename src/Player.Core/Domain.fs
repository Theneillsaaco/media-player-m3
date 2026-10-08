namespace Player.Core

type TrackKind =
    | Song
    | Video
    
type Track =
    { Id: string
      Title: string
      Artist: string
      Album: string
      DurationSeconds: float
      ArtUrl: string
      Kind: TrackKind }
    
type RepeatMode =
    | Off
    | One
    | All