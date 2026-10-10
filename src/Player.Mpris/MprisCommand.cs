namespace Player.Mpris;

/// <summary>A request from the desktop. Number is microseconds, a 0..1 volume or 0/1 for shuffle; Text is the loop status.</summary>
public readonly record struct MprisCommand(MprisAction Action, double Number = 0, string Text = "");
