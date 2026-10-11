namespace Player.Core.Domain

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
    
