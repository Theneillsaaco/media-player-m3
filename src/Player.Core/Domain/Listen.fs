namespace Player.Core.Domain

type Listen = {
    StartedAt: int64
    Artist: string
    Title: string
    Album: string
    DurationSeconds: float
}