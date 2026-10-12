module ResumeTests

open Player.Core.Playback
open Xunit

[<Fact>]
let ``parse knows the three values and defaults to from start`` () =
    Assert.Equal(StartupMode.Continue, Resume.parse "continue")
    Assert.Equal(StartupMode.FromStart, Resume.parse "fromStart")
    Assert.Equal(StartupMode.NoRestore, Resume.parse "none")
    Assert.Equal(StartupMode.FromStart, Resume.parse "")
    Assert.Equal(StartupMode.FromStart, Resume.parse null)
    Assert.Equal(StartupMode.FromStart, Resume.parse "junk")

[<Fact>]
let ``toSetting round trips`` () =
    for mode in [ StartupMode.Continue; StartupMode.FromStart; StartupMode.NoRestore ] do
        Assert.Equal(mode, Resume.parse (Resume.toSetting mode))

[<Fact>]
let ``continue keeps the saved position`` () =
    Assert.Equal({ Queue = true; Seconds = 42.5 }, Resume.plan StartupMode.Continue 42.5)
    Assert.Equal({ Queue = true; Seconds = 0.0 }, Resume.plan StartupMode.Continue -3.0)

[<Fact>]
let ``from start keeps the queue and drops the position`` () =
    Assert.Equal({ Queue = true; Seconds = 0.0 }, Resume.plan StartupMode.FromStart 42.5)

[<Fact>]
let ``no restore drops both`` () =
    Assert.Equal({ Queue = false; Seconds = 0.0 }, Resume.plan StartupMode.NoRestore 42.5)
