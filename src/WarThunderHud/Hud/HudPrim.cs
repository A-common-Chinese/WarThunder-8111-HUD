namespace WtHud.Hud;

public enum HudAlign { Left, Center, Right }
public enum HudPrimKind { Line, Polyline, Circle, Arc, Text }
public enum HudTextStyle { Plain, Boxed, Caret }

public readonly struct HudColor
{
    public byte R { get; init; }
    public byte G { get; init; }
    public byte B { get; init; }
    public byte A { get; init; }
    public HudColor(byte r, byte g, byte b, byte a = 255) { R = r; G = g; B = b; A = a; }
}

/// <summary>Screen-space drawing command produced by the geometry layer.</summary>
public sealed class HudPrim
{
    public HudPrimKind Kind { get; init; }
    public List<(double X, double Y)> Points { get; } = new();
    public string Text { get; init; } = "";
    public double Radius { get; init; }
    public double StartDeg { get; init; }
    public double SweepDeg { get; init; }
    public double Width { get; init; } = 2;
    public HudAlign Align { get; init; } = HudAlign.Center;
    public double FontSize { get; init; } = 18;
    public HudTextStyle TextStyle { get; init; } = HudTextStyle.Plain;
    public HudColor Color { get; init; }
    public bool Filled { get; init; }
}
