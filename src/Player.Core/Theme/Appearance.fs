namespace Player.Core.Theme

open System.Text

/// The appearance preferences (all plain strings, which is what the QML bridge handles best) and the pure
/// steps from them to the color tokens the UI paints with.
module Appearance =
    let modeKey = "theme.mode"
    let accentKey = "theme.accent"
    let variantKey = "theme.variant"
    let contrastKey = "theme.contrast"
    
    /// Material baseline purple: with Tonal spot it gives the colors the UI was first drawn with.
    let defaultAccent = "#00658e"
    
    /// "light", "dark" or "system" (the default).
    let normalizeMode (value: string) =
        match value with
        | "light" -> "light"
        | "dark" -> "dark"
        | _ -> "system"

    /// A valid "#rrggbb" in lower case, or the default accent.
    let normalizeAccent (value: string) =
        match Color.tryParse value with
        | ValueSome color -> Color.toHex color
        | ValueNone -> defaultAccent
    
    let isAccent (value: string) = (Color.tryParse value).IsSome

    /// One of ColorScheme.variantNames; Tonal spot when unknown.
    let normalizeVariant (value: string) = ColorScheme.variantName (ColorScheme.parseVariant value)

    /// "standard" (default), "medium" or "high".
    let normalizeContrast (value: string) =
        match value with
        | "medium" -> "medium"
        | "high" -> "high"
        | _ -> "standard"

    let contrastLevel (value: string) =
        match normalizeContrast value with
        | "medium" -> 0.5
        | "high" -> 1.0
        | _ -> 0.0

    /// Whether the UI is dark. "system" follows what the desktop reports (light until it says otherwise).
    let isDark (mode: string) (systemDark: bool) =
        match normalizeMode mode with
        | "dark" -> true
        | "light" -> false
        | _ -> systemDark

    /// Every color role as a JSON object of "#rrggbb" strings, ready for JSON.parse in QML.
    let tokens (accent: string) (dark: bool) (variant: string) (contrast: string) : string =
        let source = Color.tryParse (normalizeAccent accent)
        let roles = ColorScheme.build source.Value dark (ColorScheme.parseVariant variant) (contrastLevel contrast)
        let json = StringBuilder("{")
        let mutable first = true

        for KeyValue(name, color) in roles do
            if not first then json.Append ',' |> ignore
            first <- false
            json.Append('"').Append(name).Append("\":\"").Append(Color.toHex color).Append('"') |> ignore

        json.Append('}').ToString()
