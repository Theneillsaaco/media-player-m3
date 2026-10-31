namespace Player.Core.Domain

type Listen = {
    StartedAt: int64
    Artist: string
    Title: string
    Album: string
    DurationSeconds: float
    /// The user picked this track. False for what autoplay added, which Last.fm wants to know.
    Chosen: bool
}
