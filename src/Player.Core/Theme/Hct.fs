namespace Player.Core.Theme

open System

/// An sRGB color with 16 bits per channel, the way Qt's QColor keeps it. Staying on 16 bits is what
/// makes the results identical to the C++ solver this is ported from (Sung's m3color.cpp, MIT).
[<Struct>]
type Rgb = {
    R: uint16
    G: uint16
    B: uint16
}

/// A color measured in HCT. Hue is degrees, chroma reaches about 120 inside sRGB, tone is CIE L* (0-100).
type Hct = {
    Hue: float
    Chroma: float
    Tone: float
}

/// Material 3 color math: CAM16 hue and chroma over CIE L* tone (HCT). Tone is L*, so the tone numbers
/// the guidelines quote are what carry the contrast guarantees. `solve` reproduces the requested tone and
/// hue exactly and steps the chroma down until the color fits inside sRGB.
module Color =
    let private pi = 3.14159265358979323846
    
    let internal bound (low: float) (value: float) (high: float) =
        Math.Max(low, min high value)
        
    let internal sanitizeDegrees (degrees: float) =
        let d = degrees % 360.0
        if d < 0.0 then d + 360.0 else d
        
    /// std::round: halves go away from zero (Math.Round alone rounds them to even).
    let internal round (value: float) =
        Math.Round(value, MidpointRounding.AwayFromZero)
        
    // RGB
    
    let private channel16 (value: float) =
        // QColor::fromRgbF takes floats and rounds on float arithmetic: qRound(r * USHRT_MAX).
        let scaled = float32 value * 65535.0f
        uint16 (int (scaled + 0.5f))
        
    let private ofUnit (r: float) (g: float) (b: float) =
        { R = channel16 r; G = channel16 g; B = channel16 b }
       
    /// QColor::redF and friends: a float division, widened to double.
    let private unitOf (channel: uint16) =
        float (float32 channel / 65535.0f)
        
    let ofBytes (r: int) (g: int) (b: int) =
        { R = uint16 (r * 257); G = uint16 (g * 257); B = uint16 (b * 257) }
        
    /// "#rrggbb" or "rrggbb".
    let tryParse (text: string) : Rgb voption =
        let hex = if isNull text then "" else text.Trim().TrimStart('#')
        let isHex (c: char) = Uri.IsHexDigit c
        
        if hex.Length = 6 && Seq.forall isHex hex then
            let part (i: int) = Convert.ToInt32(hex.Substring(i, 2), 16)
            ValueSome(ofBytes (part 0) (part 2) (part 4))
        else
            ValueNone
            
    /// Eight bits per channel, rounded the way QColor::name does.
    let private toByte (channel: uint16) =
        int (Math.Round(float channel / 257.0, MidpointRounding.AwayFromZero))
        
    let toHex (c: Rgb) = $"#{toByte c.R:x2}{toByte c.G:x2}{toByte c.B:x2}"
    
    // transfer functions
    
    let private linearized (v: float) =
        100.0 * (if v <= 0.040449936 then v / 12.92 else Math.Pow((v + 0.055) / 1.055, 2.4))
        
    let private delinearized (value: float) =
        let v = bound 0.0 (value / 100.0) 1.0
        if v <= 0.0031308 then v * 12.92 else 1.055 * Math.Pow(v, 1.0 / 2.4) - 0.055

    let private epsilon = 216.0 / 24389.0

    let internal labF (t: float) =
        if t > epsilon then Math.Cbrt t else (24389.0 / 27.0 * t + 16.0) / 116.0
        
    let private labInverseF (ft: float) =
        let cubed = ft * ft * ft
        if cubed > epsilon then cubed else (116.0 * ft - 16.0) / (24389.0 / 27.0)
        
    let private yFromTone (tone: float) = 100.0 * labInverseF ((tone + 16.0) / 116.0)
    let private toneFromY (y: float) = 116.0 * labF (y / 100.0) - 16.0
    
    let internal luminanceY (c: Rgb) =
        0.2126 * linearized (unitOf c.R) + 0.7152 * linearized (unitOf c.G) + 0.0722 * linearized (unitOf c.B)

    let private fromLinearRgb (r: float) (g: float) (b: float) =
        ofUnit (bound 0.0 (delinearized r) 1.0) (bound 0.0 (delinearized g) 1.0) (bound 0.0 (delinearized b) 1.0)

    // CAM16 

    /// The viewing conditions Material measures colors under: a D65 white point, an L* 50
    /// background and an average surround.
    type private Viewing = {
        Aw: float
        Nbb: float
        Ncb: float
        C: float
        Nc: float
        N: float
        Fl: float
        Z: float
        RgbD: float[]
    }
    
    let private viewing =
        let whiteX, whiteY, whiteZ = 95.047, 100.0, 108.883
        let rW = 0.401288 * whiteX + 0.650173 * whiteY - 0.051461 * whiteZ
        let gW = -0.250268 * whiteX + 1.204414 * whiteY + 0.045854 * whiteZ
        let bW = -0.002079 * whiteX + 0.048952 * whiteY + 0.953127 * whiteZ
        let surround = 2.0
        let f = 0.8 + surround / 10.0
        
        let c =
            if f >= 0.9 then
                0.59 + (0.69 - 0.59) * ((f - 0.9) * 10.0)
            else
                0.525 + (0.59 - 0.525) * ((f - 0.8) * 10.0)
        
        let adaptingLuminance = (200.0 / pi) * yFromTone 50.0 / 100.0
        let d = bound 0.0 (f * (1.0 - (1.0 / 3.6) * Math.Exp((-adaptingLuminance - 42.0) / 92.0))) 1.0
        let k = 1.0 / (5.0 * adaptingLuminance + 1.0)
        let k4 = k * k * k * k
        let k4F = 1.0 - k4
        let fl = k4 * adaptingLuminance + 0.1 * k4F * k4F * Math.Cbrt(5.0 * adaptingLuminance)
        let n = yFromTone 50.0 / whiteY
        let z = 1.48 + Math.Sqrt n
        let nbb = 0.725 / Math.Pow(n, 0.2)
        let rgbD = [| d * (100.0 / rW) + 1.0 - d; d * (100.0 / gW) + 1.0 - d; d * (100.0 / bW) + 1.0 - d |]

        let factor (i: int) (white: float) = Math.Pow(fl * rgbD[i] * white / 100.0, 0.42)
        let rgbA (x: float) = 400.0 * x / (x + 27.13)
        let ra, ga, ba = rgbA (factor 0 rW), rgbA (factor 1 gW), rgbA (factor 2 bW)

        { Aw = (2.0 * ra + ga + 0.05 * ba) * nbb
          Nbb = nbb
          Ncb = nbb
          C = c
          Nc = f
          N = n
          Fl = fl
          Z = z
          RgbD = rgbD }


    let private adapt (value: float) =
        let af = Math.Pow(viewing.Fl * Math.Abs value / 100.0, 0.42)
        Math.CopySign(400.0 * af / (af + 27.13), value)

    let private unadapt (adapted: float) =
        let magnitude = Math.Abs adapted
        let b = Math.Max(0.0, 27.13 * magnitude / (400.0 - magnitude))
        Math.CopySign(Math.Pow(b, 1.0 / 0.42), adapted)

    /// The color whose CAM16 hue and chroma are the ones asked for and whose tone lands on the
    /// requested Y, or ValueNone when sRGB cannot hold it.
    let private resultByJ (hueRadians: float) (chroma: float) (y: float) : Rgb voption =
        let vc = viewing
        let tInnerCoeff = 1.0 / Math.Pow(1.64 - Math.Pow(0.29, vc.N), 0.73)
        let eHue = 0.25 * (Math.Cos(hueRadians + 2.0) + 3.8)
        let p1 = eHue * (50000.0 / 13.0) * vc.Nc * vc.Ncb
        let hSin = Math.Sin hueRadians
        let hCos = Math.Cos hueRadians

        let rec go (round: int) (j: float) =
            if round >= 5 then
                ValueNone
            else
                let jNormalized = j / 100.0
                let alpha = if chroma = 0.0 || j = 0.0 then 0.0 else chroma / Math.Sqrt jNormalized
                let t = Math.Pow(alpha * tInnerCoeff, 1.0 / 0.9)
                let ac = vc.Aw * Math.Pow(jNormalized, 1.0 / vc.C / vc.Z)
                let p2 = ac / vc.Nbb
                let gamma = 23.0 * (p2 + 0.305) * t / (23.0 * p1 + 11.0 * t * hCos + 108.0 * t * hSin)
                let a = gamma * hCos
                let b = gamma * hSin
                let rA = (460.0 * p2 + 451.0 * a + 288.0 * b) / 1403.0
                let gA = (460.0 * p2 - 891.0 * a - 261.0 * b) / 1403.0
                let bA = (460.0 * p2 - 220.0 * a - 6300.0 * b) / 1403.0
                let rS = unadapt rA
                let gS = unadapt gA
                let bS = unadapt bA
                // Undiscounted CAM16 response back to linear sRGB, in one step.
                let red = 1373.2198709594231 * rS - 1100.4251190754821 * gS - 7.278681089101213 * bS
                let green = -271.815969077903 * rS + 559.6580465940733 * gS - 32.46047482791194 * bS
                let blue = 1.9622899599665666 * rS - 57.173814538844006 * gS + 308.7233197812385 * bS

                if red < 0.0 || green < 0.0 || blue < 0.0 then
                    ValueNone
                else
                    let fnj = 0.2126 * red + 0.7152 * green + 0.0722 * blue

                    if fnj <= 0.0 then
                        ValueNone
                    elif round = 4 || Math.Abs(fnj - y) < 0.002 then
                        if red > 100.01 || green > 100.01 || blue > 100.01 then
                            ValueNone
                        else
                            ValueSome(fromLinearRgb red green blue)
                    else
                        go (round + 1) (j - (fnj - y) * j / (2.0 * fnj))

        go 0 (Math.Sqrt y * 11.0)

    // public

    /// L* of a color, on the same 0-100 scale as tone.
    let toneOf (c: Rgb) = toneFromY (luminanceY c)

    /// WCAG contrast between two colors.
    let contrastRatio (a: Rgb) (b: Rgb) =
        let x = luminanceY a
        let y = luminanceY b
        (Math.Max(x, y) / 100.0 + 0.05) / (Math.Min(x, y) / 100.0 + 0.05)

    let measure (c: Rgb) : Hct =
        let vc = viewing
        let r = linearized (unitOf c.R)
        let g = linearized (unitOf c.G)
        let b = linearized (unitOf c.B)
        let x = 0.41233895 * r + 0.35762064 * g + 0.18051042 * b
        let y = 0.2126 * r + 0.7152 * g + 0.0722 * b
        let z = 0.01932141 * r + 0.11916382 * g + 0.95034478 * b
        let rC = 0.401288 * x + 0.650173 * y - 0.051461 * z
        let gC = -0.250268 * x + 1.204414 * y + 0.045854 * z
        let bC = -0.002079 * x + 0.048952 * y + 0.953127 * z
        let rA = adapt (vc.RgbD[0] * rC)
        let gA = adapt (vc.RgbD[1] * gC)
        let bA = adapt (vc.RgbD[2] * bC)
        let a = (11.0 * rA - 12.0 * gA + bA) / 11.0
        let bb = (rA + gA - 2.0 * bA) / 9.0
        let u = (20.0 * rA + 20.0 * gA + 21.0 * bA) / 20.0
        let p2 = (40.0 * rA + 20.0 * gA + bA) / 20.0
        let hue = sanitizeDegrees (Math.Atan2(bb, a) * 180.0 / pi)
        let j = 100.0 * Math.Pow(p2 * vc.Nbb / vc.Aw, vc.C * vc.Z)
        let huePrime = if hue < 20.14 then hue + 360.0 else hue
        let eHue = 0.25 * (Math.Cos(huePrime * pi / 180.0 + 2.0) + 3.8)
        let t = eHue * (50000.0 / 13.0) * vc.Nc * vc.Ncb * Math.Sqrt(a * a + bb * bb) / (u + 0.305)
        let alpha = Math.Pow(t, 0.9) * Math.Pow(1.64 - Math.Pow(0.29, vc.N), 0.73)
        { Hue = hue; Chroma = alpha * Math.Sqrt(j / 100.0); Tone = toneFromY y }

    /// The sRGB color closest to this hue and chroma at exactly this tone.
    let solve (hue: float) (chroma: float) (tone: float) : Rgb =
        let tone = bound 0.0 tone 100.0
        let y = yFromTone tone
        let grey = bound 0.0 (delinearized y) 1.0

        if chroma < 0.0001 || tone < 0.0001 || tone > 99.9999 then
            ofUnit grey grey grey
        else
            let hueRadians = sanitizeDegrees hue * pi / 180.0

            // Approach the requested chroma from above; the first value sRGB can hold is the answer.
            let rec go (c: float) =
                if c > 0.0 then
                    match resultByJ hueRadians c y with
                    | ValueSome found -> found
                    | ValueNone -> go (c - 0.25)
                else
                    ofUnit grey grey grey

            go chroma

    /// Tone of Y, for the contrast math in the scheme builder.
    let internal toneOfY (y: float) = toneFromY y

    let internal yOfTone (tone: float) = yFromTone tone

    /// The Lab-based "temperature" Material uses to pick analogous colors.
    let internal rawTemperature (c: Rgb) =
        let r = linearized (unitOf c.R)
        let g = linearized (unitOf c.G)
        let b = linearized (unitOf c.B)
        let x = 0.41233895 * r + 0.35762064 * g + 0.18051042 * b
        let y = 0.2126 * r + 0.7152 * g + 0.0722 * b
        let z = 0.01932141 * r + 0.11916382 * g + 0.95034478 * b
        let a = 500.0 * (labF (x / 95.047) - labF (y / 100.0))
        let bb = 200.0 * (labF (y / 100.0) - labF (z / 108.883))
        let hue = sanitizeDegrees (Math.Atan2(bb, a) * 180.0 / pi)
        let chroma = Double.Hypot(a, bb)
        -0.5 + 0.02 * Math.Pow(chroma, 1.07) * Math.Cos(sanitizeDegrees (hue - 50.0) * pi / 180.0)
