namespace Player.Core.Theme

open System
open System.Collections.Concurrent

/// Material's scheme variants: how much of the source color the interface takes.
type Variant =
    | Neutral
    | TonalSpot
    | Vibrant
    | Expressive
    | Content
    
/// One Material tonal palette: a fixed hue and chroma read at any tone.
type TonalPalette = {
    Hue: float
    Chroma: float
}

type Palettes = {
    Primary: TonalPalette
    Secondary: TonalPalette
    Tertiary: TonalPalette
    Neutral: TonalPalette
    NeutralVariant: TonalPalette
    /// The one palette that does not follow the source: a warning reads as a warning whatever the artwork.
    Error: TonalPalette
}

/// Port of Sung's m3color.cpp (MIT), which follows Material Color Utilities (color_spec_2021). The comments
/// that cite MCU files and lines are Sung's.
module ColorScheme =
    let private tone (palette: TonalPalette) (value: float) =
        Color.solve palette.Hue palette.Chroma value
        
    let parseVariant (name: string) =
        match name with
        | "neutral" -> Neutral
        | "vibrant" -> Vibrant
        | "expressive" -> Expressive
        | "content" -> Content
        | _ -> TonalSpot
        
    let variantName (variant: Variant) =
        match variant with
        | Neutral -> "neutral"
        | TonalSpot -> "tonalSpot"
        | Vibrant -> "vibrant"
        | Expressive -> "expressive"
        | Content -> "content"
        
    let variantNames = [| "neutral"; "tonalSpot"; "vibrant"; "expressive"; "content" |]
    
    // palettes

    /// Material turns the secondary and tertiary hues by an amount that depends on where the source sits on the
    /// wheel, so a scheme stays balanced whatever it is given. The breaks and the rotations are Material's own.
    let private rotatedHue (hue: float) (breaks: float[]) (rotations: float[]) =
        let mutable result = hue
        let mutable found = false
        
        for i in 0 .. breaks.Length - 2 do
            if not found && breaks[i] <= hue && hue < breaks[i + 1] then
                result <- Color.sanitizeDegrees (hue + rotations[i])
                found <- true
        
        result
        
    let private vibrantBreaks = [| 0.0; 41.0; 61.0; 101.0; 131.0; 181.0; 251.0; 301.0; 360.0 |]
    let private expressiveBreaks = [| 0.0; 21.0; 51.0; 121.0; 151.0; 191.0; 271.0; 321.0; 360.0 |]

    /// MCU color_spec_2021.ts:40-72 and :429-449 move Content's secondary container toward the tone where its
    /// palette can hold more chroma.
    let private desiredChromaTone (palette: TonalPalette) (initial: float) (decreasing: bool) =
        let mutable answer = initial
        let mutable closest = Color.measure (tone palette answer)
        
        if closest.Chroma >= palette.Chroma then
            answer
        else
            let mutable peak = closest.Chroma
            let mutable searching = true
            
            while searching && closest.Chroma < palette.Chroma do
                answer <- answer + (if decreasing then -1.0 else 1.0)
                
                if answer < 0.0 || answer > 100.0 then
                    searching <- false
                else
                    let possible = Color.measure (tone palette answer)
                    
                    if peak > possible.Chroma || Math.Abs(possible.Chroma - palette.Chroma) < 0.4 then
                        searching <- false
                    else
                        if Math.Abs(possible.Chroma - palette.Chroma) < Math.Abs(closest.Chroma - palette.Chroma) then
                            closest <- possible
                            
                        peak <- max peak possible.Chroma

            Color.bound 0.0 answer 100.0

    /// MCU dislike_analyzer.ts:41-62 rounds the achieved HCT hue, chroma and tone before deciding whether a dark
    /// yellow-green needs tone 70.
    let private isDisliked (color: Hct) =
        Color.round color.Hue >= 90.0
        && Color.round color.Hue <= 111.0
        && Color.round color.Chroma >= 16.0
        && Color.round color.Tone < 65.0
        
    let private contentCache = ConcurrentDictionary<Rgb, TonalPalette>()
    
    /// MCU dynamic_scheme.ts:663-670 chooses analogous(3, 6)[2]. temperature_cache.ts:67-145 divides the full wheel
    /// by cumulative absolute temperature change; the third analogue is the first clockwise sixth.
    let private contentTertiary (source: Rgb) (sourceHct: Hct) =
        match contentCache.TryGetValue source with
        | true, found -> found
        | _ ->
            let start = int (Color.round sourceHct.Hue) % 360
            let temperatures =
                Array.init 360 (fun hue -> Color.rawTemperature (Color.solve (float hue) sourceHct.Chroma sourceHct.Tone))

            let mutable total = 0.0

            for step in 1..360 do
                total <- total + Math.Abs(temperatures[(start + step) % 360] - temperatures[(start + step - 1) % 360])

            let mutable traveled = 0.0
            let mutable analogue = start
            let mutable step = 1
            let mutable searching = true

            while searching && step <= 360 do
                let hue = (start + step) % 360
                traveled <- traveled + Math.Abs(temperatures[hue] - temperatures[(start + step - 1) % 360])

                if traveled >= total / 6.0 then
                    analogue <- hue
                    searching <- false

                step <- step + 1

            let mutable candidate = Color.measure (Color.solve (float analogue) sourceHct.Chroma sourceHct.Tone)

            // MCU dislike_analyzer.ts:41-62 lightens non-neutral dark yellow-greens.
            if isDisliked candidate then
                candidate <- Color.measure (Color.solve candidate.Hue candidate.Chroma 70.0)

            let palette = { Hue = candidate.Hue; Chroma = candidate.Chroma }

            if contentCache.Count >= 64 then
                contentCache.Clear()

            contentCache[source] <- palette
            palette

    let palettesFor (source: Rgb) (variant: Variant) : Palettes =
        // Every variant is the same five palettes spread differently. The neutral ones carry a trace of the
        // source hue, which is what tints the surfaces without colouring them.
        let hct = Color.measure source
        let hue = hct.Hue
        let chroma = hct.Chroma
        let p h c = { Hue = h; Chroma = c }
        let error = p 25.0 84.0

        match variant with
        | Neutral ->
            { Primary = p hue 12.0
              Secondary = p hue 8.0
              Tertiary = p hue 16.0
              Neutral = p hue 2.0
              NeutralVariant = p hue 2.0
              Error = error }
        | Vibrant ->
            { Primary = p hue 200.0
              Secondary = p (rotatedHue hue vibrantBreaks [| 18.0; 15.0; 10.0; 12.0; 15.0; 18.0; 15.0; 12.0; 12.0 |]) 24.0
              Tertiary = p (rotatedHue hue vibrantBreaks [| 35.0; 30.0; 20.0; 25.0; 30.0; 35.0; 30.0; 25.0; 25.0 |]) 32.0
              Neutral = p hue 10.0
              NeutralVariant = p hue 12.0
              Error = error }
        | Expressive ->
            { Primary = p (Color.sanitizeDegrees (hue + 240.0)) 40.0
              Secondary = p (rotatedHue hue expressiveBreaks [| 45.0; 95.0; 45.0; 20.0; 45.0; 90.0; 45.0; 45.0; 45.0 |]) 24.0
              Tertiary = p (rotatedHue hue expressiveBreaks [| 120.0; 120.0; 20.0; 45.0; 20.0; 15.0; 20.0; 120.0; 120.0 |]) 32.0
              Neutral = p (Color.sanitizeDegrees (hue + 15.0)) 8.0
              NeutralVariant = p (Color.sanitizeDegrees (hue + 15.0)) 12.0
              Error = error }
        | Content ->
            // MCU dynamic_scheme.ts:602-635, 663-670, 700-733: Content retains the source's chroma and takes
            // its tertiary from a temperature analogue.
            { Primary = p hue chroma
              Secondary = p hue (Math.Max(chroma - 32.0, chroma * 0.5))
              Tertiary = contentTertiary source hct
              Neutral = p hue (chroma / 8.0)
              NeutralVariant = p hue (chroma / 8.0 + 4.0)
              Error = error }
        | TonalSpot ->
            { Primary = p hue 36.0
              Secondary = p hue 16.0
              Tertiary = p (Color.sanitizeDegrees (hue + 60.0)) 24.0
              Neutral = p hue 6.0
              NeutralVariant = p hue 8.0
              Error = error }

    // ---- contrast ----

    // MCU contrast_curve.ts interpolates each role's four published ratios.
    type private Curve =
        { Low: float
          Normal: float
          Medium: float
          High: float }

    let private curveAt (level: float) (c: Curve) =
        if level <= -1.0 then c.Low
        elif level < 0.0 then c.Low + (c.Normal - c.Low) * (level + 1.0)
        elif level < 0.5 then c.Normal + (c.Medium - c.Normal) * (level / 0.5)
        elif level < 1.0 then c.Medium + (c.High - c.Medium) * ((level - 0.5) / 0.5)
        else c.High

    let private curve low normal medium high = { Low = low; Normal = normal; Medium = medium; High = high }

    let private toneContrast (a: float) (b: float) =
        let x = Color.yOfTone a / 100.0
        let y = Color.yOfTone b / 100.0
        (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05)

    // MCU contrast.ts:63-146 converts the target ratio to a tone, adding or subtracting 0.4 to allow for gamut
    // mapping. dynamic_color.ts:319-358 chooses the better side and prefers light ink below rounded T60.
    let private foregroundTone (background: float) (ratio: float) =
        let backgroundY = Color.yOfTone background
        let lightY = ratio * (backgroundY + 5.0) - 5.0
        let darkY = (backgroundY + 5.0) / ratio - 5.0
        let lightCandidate = Color.toneOfY lightY + 0.4
        let darkCandidate = Color.toneOfY darkY - 0.4
        let lighter = if lightCandidate >= 0.0 && lightCandidate <= 100.0 then lightCandidate else 100.0
        let darker = if darkCandidate >= 0.0 && darkCandidate <= 100.0 then darkCandidate else 0.0
        let lighterRatio = toneContrast lighter background
        let darkerRatio = toneContrast darker background

        if Color.round background < 60.0 then
            let nearlyEqualMiss =
                Math.Abs(lighterRatio - darkerRatio) < 0.1 && lighterRatio < ratio && darkerRatio < ratio

            if lighterRatio >= ratio || lighterRatio >= darkerRatio || nearlyEqualMiss then lighter else darker
        elif darkerRatio >= ratio || darkerRatio >= lighterRatio then
            darker
        else
            lighter

    // MCU dynamic_color.ts:388-548 keeps a role's normal tone when it already clears its curve, otherwise it
    // searches away from its background. Sung also checks every supplied background because the same ink spans
    // its surface ladder. Searching in tone space avoids repeated HCT gamut solves.
    let private toneFor (initial: float) (lighter: bool) (backgrounds: float list) (target: float) =
        let clears (t: float) = backgrounds |> List.forall (fun bg -> not (toneContrast t bg + 0.0001 < target))

        if clears initial then
            initial
        else
            let search (direction: float) =
                let mutable t = initial
                let mutable found = -1.0

                while found < 0.0 && t >= 0.0 && t <= 100.0 do
                    if clears t then found <- Color.bound 0.0 t 100.0 else t <- t + direction * 0.25

                found

            let first = search (if lighter then 1.0 else -1.0)

            if first >= 0.0 then
                first
            else
                let second = search (if lighter then -1.0 else 1.0)

                if second >= 0.0 then
                    second
                else
                    let weakest (t: float) =
                        backgrounds |> List.fold (fun ratio bg -> Math.Min(ratio, toneContrast t bg)) 21.0

                    if weakest 100.0 > weakest 0.0 then 100.0 else 0.0

    type private Pair = { Nearer: float; Farther: float }

    // MCU dynamic_color.ts:392-479: solve each member against the common background, expand the farther member
    // to ten tones, then avoid T50-59.
    let private resolvePair
        (nearer0: float)
        (farther0: float)
        (dark: bool)
        (background: float)
        (nearRatio: float)
        (farRatio: float)
        (stayTogether: bool)
        =
        let delta = 10.0
        let direction = if dark then 1.0 else -1.0
        let mutable nearer = nearer0
        let mutable farther = farther0

        if toneContrast background nearer < nearRatio then
            nearer <- foregroundTone background nearRatio

        if toneContrast background farther < farRatio then
            farther <- foregroundTone background farRatio

        if (farther - nearer) * direction < delta then
            farther <- Color.bound 0.0 (nearer + delta * direction) 100.0

            if (farther - nearer) * direction < delta then
                nearer <- Color.bound 0.0 (farther - delta * direction) 100.0

        let awkward (t: float) = t >= 50.0 && t < 60.0

        if awkward nearer || (stayTogether && awkward farther) then
            if dark then
                nearer <- 60.0
                farther <- Math.Max(farther, nearer + delta)
            else
                nearer <- 49.0
                farther <- Math.Min(farther, nearer - delta)
        elif awkward farther then
            farther <- if dark then 60.0 else 49.0

        { Nearer = nearer; Farther = farther }

    type private Accent = { Accent: float; Container: float }

    // MCU color_spec_2021.ts:325-326 pairs Container as nearer and Accent as farther, with stayTogether false.
    let private accentTones accent container dark background accentRatio containerRatio =
        let pair = resolvePair container accent dark background containerRatio accentRatio false
        { Accent = pair.Farther; Container = pair.Nearer }

    // MCU color_spec_2021.ts:604-740 uses ToneDeltaPair(Fixed, FixedDim, 10, lighter, true): Fixed is nearer in
    // light, FixedDim nearer in dark. If their gap contracts in dark, round two moves the farther Fixed role.
    let private fixedTones dark background ratio =
        if dark then
            let pair = resolvePair 80.0 90.0 true background ratio ratio true
            { Accent = pair.Farther; Container = pair.Nearer }
        else
            let pair = resolvePair 90.0 80.0 false background ratio ratio true
            { Accent = pair.Nearer; Container = pair.Farther }

    // MCU color_spec_2021.ts:130-739 publishes these distinct role curves.
    let private onSurfaceCurve = curve 4.5 7.0 11.0 21.0
    let private onSurfaceVariantCurve = curve 3.0 4.5 7.0 11.0
    let private primaryCurve = curve 3.0 4.5 7.0 7.0
    let private containerCurve = curve 1.0 1.0 3.0 4.5
    let private onContainerCurve = curve 3.0 4.5 7.0 11.0
    let private onAccentCurve = curve 4.5 7.0 11.0 21.0
    let private fixedVariantCurve = curve 3.0 4.5 7.0 11.0
    let private outlineCurve = curve 1.5 3.0 4.5 7.0
    let private outlineVariantCurve = curve 1.0 1.0 3.0 4.5
    let private onBackgroundCurve = curve 3.0 3.0 4.5 7.0

    let private capital (name: string) = string (Char.ToUpperInvariant name[0]) + name.Substring 1

    /// Every color role the interface uses, keyed by its Material name. `contrast` runs from 0 (Material's
    /// standard tones) through 0.5 (medium) to 1 (high); surface containers, accents, fixed roles and text each
    /// follow their own curve.
    let build (source: Rgb) (dark: bool) (variant: Variant) (contrast0: float) : Map<string, Rgb> =
        let p = palettesFor source variant
        let contrast = Color.bound 0.0 contrast0 1.0
        let at (c: Curve) = curveAt contrast c
        let mutable roles: Map<string, Rgb> = Map.empty
        let put (name: string) (color: Rgb) = roles <- roles.Add(name, color)

        // MCU color_spec_2021.ts:130-221. The contrast ladder moves independently of the source palette.
        let surface = if dark then 6.0 else 98.0
        let low = if dark then 10.0 + 2.0 * contrast else (if contrast <= 0.5 then 96.0 else 96.0 - 2.0 * (contrast - 0.5))

        let dim =
            if dark then 6.0
            else (if contrast <= 0.5 then 87.0 - 14.0 * contrast else 80.0 - 10.0 * (contrast - 0.5))

        let bright =
            if dark then (if contrast <= 0.5 then 24.0 + 10.0 * contrast else 29.0 + 10.0 * (contrast - 0.5))
            else 98.0

        let lowest = if dark then 4.0 - 4.0 * contrast else 100.0

        let middle =
            if dark then (if contrast <= 0.5 then 12.0 + 8.0 * contrast else 16.0 + 8.0 * (contrast - 0.5))
            else 94.0 - 4.0 * contrast

        let high =
            if dark then (if contrast <= 0.5 then 17.0 + 8.0 * contrast else 21.0 + 8.0 * (contrast - 0.5))
            else (if contrast <= 0.5 then 92.0 - 8.0 * contrast else 88.0 - 6.0 * (contrast - 0.5))

        let highest =
            if dark then (if contrast <= 0.5 then 22.0 + 8.0 * contrast else 26.0 + 8.0 * (contrast - 0.5))
            else (if contrast <= 0.5 then 90.0 - 12.0 * contrast else 84.0 - 8.0 * (contrast - 0.5))

        let adjacent = if dark then bright else dim // highestSurface in MCU.

        for (name, value) in
            [ "background", surface
              "surface", surface
              "surfaceDim", dim
              "surfaceBright", bright
              "surfaceContainerLowest", lowest
              "surfaceContainerLow", low
              "surfaceContainer", middle
              "surfaceContainerHigh", high
              "surfaceContainerHighest", highest ] do
            put name (tone p.Neutral value)

        put "surfaceVariant" (tone p.NeutralVariant (if dark then 30.0 else 90.0))
        put "scrim" (tone p.Neutral 0.0)
        put "shadow" (tone p.Neutral 0.0)

        let ink (name: string) (palette: TonalPalette) (initial: float) (lighter: bool) (backgrounds: float list) (ratio: float) =
            put name (tone palette (toneFor initial lighter backgrounds ratio))

        let surfaces = [ adjacent; surface; low; middle; high; highest ]
        ink "onBackground" p.Neutral (if dark then 90.0 else 10.0) dark [ surface ] (at onBackgroundCurve)
        ink "onSurface" p.Neutral (if dark then 90.0 else 10.0) dark surfaces (at onSurfaceCurve)
        ink "onSurfaceVariant" p.NeutralVariant (if dark then 80.0 else 30.0) dark surfaces (at onSurfaceVariantCurve)
        ink "outline" p.NeutralVariant (if dark then 60.0 else 50.0) dark [ adjacent ] (at outlineCurve)
        ink "outlineVariant" p.NeutralVariant (if dark then 30.0 else 80.0) dark [ adjacent ] (at outlineVariantCurve)

        // MCU color_spec_2021.ts:250-265 and 385-392. Inverse roles have their own background and their own curves.
        let inverseSurface = if dark then 90.0 else 20.0
        put "inverseSurface" (tone p.Neutral inverseSurface)
        ink "inverseOnSurface" p.Neutral (if dark then 20.0 else 95.0) (not dark) [ inverseSurface ] (at onSurfaceCurve)
        ink "inversePrimary" p.Primary (if dark then 40.0 else 80.0) (not dark) [ inverseSurface ] (at primaryCurve)

        let family (name: string) (palette: TonalPalette) (containerInitial: float) =
            let cap = capital name
            let pair =
                accentTones (if dark then 80.0 else 40.0) containerInitial dark adjacent (at primaryCurve) (at containerCurve)

            put name (tone palette pair.Accent)
            put (name + "Container") (tone palette pair.Container)
            put ("on" + cap) (tone palette (toneFor (if dark then 20.0 else 100.0) (not dark) [ pair.Accent ] (at onAccentCurve)))

            // MCU color_spec_2021.ts:367-382, 452-467, 529-545: only Content's accent containers begin at
            // foregroundTone(container, 4.5). Error keeps its ordinary 90 dark / 30 light initial tone.
            let fidelityInk = variant = Content && name <> "error"
            let initialInk = if fidelityInk then foregroundTone containerInitial 4.5 else (if dark then 90.0 else 30.0)
            let desired = at onContainerCurve

            let resolvedInk =
                if toneContrast pair.Container initialInk >= desired then initialInk
                else foregroundTone pair.Container desired

            put ("on" + cap + "Container") (tone palette resolvedInk)

        // MCU color_spec_2021.ts:312-599. Content starts primaryContainer at the source tone; its other
        // containers search for chroma near their base tone.
        let sourceTone = Color.toneOf source
        let baseContainer = if dark then 30.0 else 90.0
        family "primary" p.Primary (if variant = Content then sourceTone else baseContainer)

        family
            "secondary"
            p.Secondary
            (if variant = Content then desiredChromaTone p.Secondary baseContainer (not dark) else baseContainer)

        // MCU color_spec_2021.ts:508-526 tests the achievable HCT colour at the source tone, then fixes the
        // disliked one before applying contrast.
        let tertiaryStart =
            if variant = Content then
                (if isDisliked (Color.measure (tone p.Tertiary sourceTone)) then 70.0 else sourceTone)
            else
                baseContainer

        family "tertiary" p.Tertiary tertiaryStart
        family "error" p.Error baseContainer
        put "surfaceTint" roles["primary"]

        let fixedFamily (name: string) (palette: TonalPalette) =
            let cap = capital name
            let pair = fixedTones dark adjacent (at containerCurve)
            let both = [ pair.Accent; pair.Container ]
            let fixedInk = toneFor 10.0 (pair.Accent < 60.0) both (at onAccentCurve)
            let variantInk = toneFor 30.0 (pair.Accent < 60.0) both (at fixedVariantCurve)
            put (name + "Fixed") (tone palette pair.Accent)
            put (name + "FixedDim") (tone palette pair.Container)
            put ("on" + cap + "Fixed") (tone palette fixedInk)
            put ("on" + cap + "FixedVariant") (tone palette variantInk)

        fixedFamily "primary" p.Primary
        fixedFamily "secondary" p.Secondary
        fixedFamily "tertiary" p.Tertiary
        roles

