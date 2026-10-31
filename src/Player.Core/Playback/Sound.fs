namespace Player.Core.Playback

open System
open System.Globalization

/// The speeds the player offers. The pitch stays (mpv corrects it).
module PlaybackSpeed =
    let steps = [| 0.5; 0.75; 1.0; 1.25; 1.5; 2.0; 3.0 |]

    /// The offered speed closest to a value; 1x for something that is not a number.
    let normalize (value: float) =
        if Double.IsNaN value then 1.0 else steps |> Array.minBy (fun step -> abs (step - value))

    /// The speed after this one, wrapping from the fastest to the slowest.
    let next (current: float) =
        let index = Array.IndexOf(steps, normalize current)
        steps[(index + 1) % steps.Length]

    /// "1×", "1.25×".
    let label (speed: float) = speed.ToString("0.##", CultureInfo.InvariantCulture) + "×"

/// Volume levelling from the loudness YouTube measured for a track.
module TrackLoudness =
    /// The most a track is turned down, in dB.
    let maxCut = 24.0

    /// The gain, in dB, that brings a track to the reference level. Only ever a cut: a quiet track stays as it is
    /// instead of being pushed into clipping.
    let gainDb (loudnessDb: float) =
        if Double.IsNaN loudnessDb || Double.IsInfinity loudnessDb then
            0.0
        else
            max (-maxCut) (min 0.0 (-loudnessDb))
