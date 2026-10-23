using System.Collections.Immutable;

namespace Siege.Presentation.Style;

public enum PanelTier { Primary, Secondary, Hint }
public enum ButtonKind { Default, Primary, Danger }
public enum ButtonState { Normal, Hover, Pressed, Disabled, Focus }

public sealed record UiPanelStyle(Rgba Fill, Rgba Border, int BorderWidthPx, int CornerRadiusPx);
public readonly record struct UiEdge(Rgba Color, int WidthPx);
public sealed record UiButtonStyle(
    Rgba Fill, bool DrawFill, UiEdge Left, UiEdge Top, UiEdge Right, UiEdge Bottom,
    Rgba Text, int CornerRadiusPx, int PaddingXPx);

/// <summary>Art-base colours with the three panel levels and semantic button states.</summary>
public static partial class UiTheme
{
    public static readonly Rgba SecondaryPanelFill = new(24, 40, 48, 205);
    public static readonly Rgba SecondaryPanelBorder = new(105, 91, 65);
    public static readonly Rgba HintFill = new(18, 30, 39, 214);
    public static readonly Rgba ButtonBorder = PanelInset;
    public static readonly Rgba ButtonDisabledBorder = new(42, 51, 55);
    public static readonly Rgba ButtonDisabledText = new(135, 142, 141);
    public static readonly Rgba TextOutline = new(0, 0, 0);

    public const int MutedTextAlphaPermille = 620;
    public const int SelectedEdgeWidthPx = 2;
    public const int ViewerMarkWidthPx = 3;
    public const int CornerRadiusPx = 3;
    public const int HintCornerRadiusPx = 6;
    public const int ButtonPaddingXPx = 10;
    public const int ButtonPaddingYPx = Space2;
    public const int HintPaddingXPx = Space5;
    public const int HintPaddingYPx = Space2;
    public const int ShowBannerPx = 45;
    public const int HintOutlinePx = 6;
    public const int ShowBannerOutlinePx = 8;

    public const int Caption = 13;
    public const int Small = 14;
    public const int Body = BodyFontPx;
    public const int Title = 17;
    public const int Banner = 19;
    public static readonly ImmutableArray<int> FontLadder = [Caption, Small, Body, Title, Banner];

    public const int Space0 = 0;
    public const int Space1 = 2;
    public const int Space2 = 4;
    public const int Space3 = 6;
    public const int Space4 = 8;
    public const int Space5 = 12;
    public static readonly ImmutableArray<int> SpaceLadder = [Space0, Space1, Space2, Space3, Space4, Space5];

    private static readonly Rgba Transparent = new(0, 0, 0, 0);

    public static UiPanelStyle PanelStyleOf(PanelTier tier) => tier switch
    {
        PanelTier.Primary => new(PanelFill, PanelBorder, BorderWidthPx, CornerRadiusPx),
        PanelTier.Secondary => new(SecondaryPanelFill, SecondaryPanelBorder, BorderWidthPx, CornerRadiusPx),
        PanelTier.Hint => new(HintFill, Transparent, 0, HintCornerRadiusPx),
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    public static UiButtonStyle ButtonStyleOf(ButtonKind kind, ButtonState state, bool selected = false)
    {
        if (selected && kind != ButtonKind.Default)
            throw new ArgumentException("Only default buttons can be selected.", nameof(selected));

        (Rgba border, Rgba text) = kind switch
        {
            ButtonKind.Default => (ButtonBorder, InfoText),
            ButtonKind.Primary => (PanelBorder, PanelTop),
            ButtonKind.Danger => (DangerText, DangerText),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if (selected) text = PanelTop;

        if (state == ButtonState.Disabled)
        {
            var side = new UiEdge(ButtonDisabledBorder, BorderWidthPx);
            return Box(ButtonDisabled, true, side,
                selected ? side with { WidthPx = SelectedEdgeWidthPx } : side, ButtonDisabledText);
        }
        if (state == ButtonState.Focus)
        {
            var edge = new UiEdge(Accent, BorderWidthPx);
            return Box(Transparent, false, edge, edge, text);
        }

        Rgba fill = state switch
        {
            ButtonState.Normal => ButtonFill,
            ButtonState.Hover => ButtonHover,
            ButtonState.Pressed => ButtonPressed,
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
        var borderEdge = new UiEdge(border, BorderWidthPx);
        return Box(fill, true, borderEdge,
            selected ? new UiEdge(PanelBorder, SelectedEdgeWidthPx) : borderEdge, text);
    }

    private static UiButtonStyle Box(Rgba fill, bool drawFill, UiEdge side, UiEdge bottom, Rgba text) =>
        new(fill, drawFill, side, side, side, bottom, text, CornerRadiusPx, ButtonPaddingXPx);
}
