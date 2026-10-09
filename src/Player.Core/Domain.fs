namespace Player.Core

type TrackKind =
    | Song
    | Video
    
type Track ={
    Id: string
    Title: string
    Artist: string
    Artists: string[]
    Album: string
    DurationSeconds: float
    ArtUrl: string
    Kind: TrackKind
}
    
type RepeatMode =
    | Off
    | One
    | All
    
type Listen = {
    StartedAt: int64
    Artist: string
    Title: string
    Album: string
    DurationSeconds: float
}