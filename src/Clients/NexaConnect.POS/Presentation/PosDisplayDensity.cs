namespace NexaConnect.POS;

public enum PosDisplayDensity { Standard, Touch }
public sealed record PosUiOptions(bool TouchFirst = false, PosDisplayDensity Density = PosDisplayDensity.Standard);

public sealed record PosDensityMetrics(double ControlHeight, double BodyFontSize, double ButtonHorizontalPadding,
    double ButtonVerticalPadding, double FieldPadding, double CardPadding, double MenuWidth, double MenuHeight)
{
    public static PosDensityMetrics For(PosDisplayDensity density) => density switch
    {
        PosDisplayDensity.Standard => new(36, 14, 12, 6, 8, 16, 160, 108),
        PosDisplayDensity.Touch => new(52, 16, 16, 10, 10, 20, 184, 136),
        _ => throw new ArgumentOutOfRangeException(nameof(density))
    };
}

public static class PosResponsiveLayout
{
    public static bool StackCash(double width) => width < 1100;
    public static bool CompactHeight(double height) => height < 720;
}
