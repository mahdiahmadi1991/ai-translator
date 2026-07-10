using System.Windows;
using System.Windows.Media;

namespace AiTranslator.App.Resources;

/// <summary>A selectable language: BCP-47 code, English + native names, and a small vector flag.</summary>
public sealed class LanguageOption
{
    public LanguageOption(string code, string english, string native, ImageSource flag)
    {
        Code = code;
        English = english;
        Native = native;
        Flag = flag;
    }

    public string Code { get; }
    public string English { get; }
    public string Native { get; }
    public ImageSource Flag { get; }

    /// <summary>Uppercase code for the muted secondary label in the dropdown (e.g. "EN", "FA").</summary>
    public string CodeUpper => Code.ToUpperInvariant();

    public override string ToString() => $"{Native} — {English}";
}

/// <summary>
/// The languages offered in Settings, each with a vector flag rendered in code (no image assets — keeps
/// the offline build self-contained and avoids WPF's lack of colour-emoji rendering for flag glyphs).
/// </summary>
public static class LanguageCatalog
{
    public static IReadOnlyList<LanguageOption> All { get; } =
    [
        new("en", "English", "English", Flags.Usa),
        new("fa", "Persian", "فارسی", Flags.Iran),
        new("ar", "Arabic", "العربية", Flags.Arabic),
        new("fr", "French", "Français", Flags.France),
        new("de", "German", "Deutsch", Flags.Germany),
        new("es", "Spanish", "Español", Flags.Spain),
        new("it", "Italian", "Italiano", Flags.Italy),
        new("pt", "Portuguese", "Português", Flags.Portugal),
        new("nl", "Dutch", "Nederlands", Flags.Netherlands),
        new("ru", "Russian", "Русский", Flags.Russia),
        new("tr", "Turkish", "Türkçe", Flags.Turkey),
        new("ur", "Urdu", "اردو", Flags.Pakistan),
        new("ps", "Pashto", "پښتو", Flags.Afghanistan),
        new("hi", "Hindi", "हिन्दी", Flags.India),
        new("zh", "Chinese", "中文", Flags.China),
        new("ja", "Japanese", "日本語", Flags.Japan),
        new("ko", "Korean", "한국어", Flags.Korea),
    ];

    /// <summary>The catalog entry for a code, or a synthesized entry (neutral flag) for unknown codes.</summary>
    public static LanguageOption Get(string code)
    {
        foreach (var option in All)
        {
            if (string.Equals(option.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return option;
            }
        }

        string upper = (code ?? string.Empty).ToUpperInvariant();
        return new LanguageOption(code ?? string.Empty, upper, upper, Flags.Unknown);
    }

    /// <summary>English display name for a code (used by the overlay's direction label).</summary>
    public static string DisplayName(string code) => Get(code).English;

    /// <summary>Native display name for a code (e.g. "فارسی"), matching the language picker.</summary>
    public static string NativeName(string code) => Get(code).Native;
}

/// <summary>Tiny vector flags (24×16, rounded) built once and frozen. Approximations, not heraldry.</summary>
internal static class Flags
{
    private const double W = 24, H = 16;

    private static readonly Color White = Rgb(0xFF, 0xFF, 0xFF);
    private static readonly Color Black = Rgb(0x1A, 0x1A, 0x1A);

    public static readonly ImageSource Iran = H3(Rgb(0x23, 0x9F, 0x40), White, Rgb(0xDA, 0x00, 0x00));
    public static readonly ImageSource Usa = UsFlag();
    public static readonly ImageSource Arabic = Solid(Rgb(0x16, 0x6F, 0x3B));
    public static readonly ImageSource France = V3(Rgb(0x00, 0x35, 0x8E), White, Rgb(0xCE, 0x14, 0x26));
    public static readonly ImageSource Germany = H3(Black, Rgb(0xDD, 0x00, 0x00), Rgb(0xFF, 0xCE, 0x00));
    public static readonly ImageSource Spain = H3(Rgb(0xAA, 0x15, 0x1C), Rgb(0xF1, 0xBF, 0x00), Rgb(0xAA, 0x15, 0x1C));
    public static readonly ImageSource Italy = V3(Rgb(0x00, 0x91, 0x46), White, Rgb(0xCE, 0x2B, 0x37));
    public static readonly ImageSource Portugal = V2(Rgb(0x00, 0x66, 0x00), Rgb(0xD8, 0x1E, 0x05), 0.4);
    public static readonly ImageSource Netherlands = H3(Rgb(0xAE, 0x1C, 0x28), White, Rgb(0x21, 0x46, 0x8B));
    public static readonly ImageSource Russia = H3(White, Rgb(0x00, 0x39, 0xA6), Rgb(0xD5, 0x2B, 0x1E));
    public static readonly ImageSource Turkey = CircleOnField(Rgb(0xE3, 0x0A, 0x17), White);
    public static readonly ImageSource Pakistan = V2(White, Rgb(0x01, 0x41, 0x1C), 0.28);
    public static readonly ImageSource Afghanistan = V3(Black, Rgb(0xBF, 0x00, 0x0D), Rgb(0x00, 0x77, 0x36));
    public static readonly ImageSource India = H3(Rgb(0xFF, 0x99, 0x33), White, Rgb(0x13, 0x88, 0x08));
    public static readonly ImageSource China = Solid(Rgb(0xDE, 0x29, 0x10));
    public static readonly ImageSource Japan = CircleOnField(White, Rgb(0xBC, 0x00, 0x2D));
    public static readonly ImageSource Korea = CircleOnField(White, Rgb(0xC6, 0x0C, 0x30));
    public static readonly ImageSource Unknown = Solid(Rgb(0x5A, 0x5A, 0x66));

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static ImageSource Solid(Color c) => Build(g => Fill(g, new Rect(0, 0, W, H), c));

    private static ImageSource H3(Color a, Color b, Color c) => Build(g =>
    {
        Fill(g, new Rect(0, 0, W, H / 3 + 0.5), a);
        Fill(g, new Rect(0, H / 3, W, H / 3 + 0.5), b);
        Fill(g, new Rect(0, 2 * H / 3, W, H / 3), c);
    });

    private static ImageSource V3(Color a, Color b, Color c) => Build(g =>
    {
        Fill(g, new Rect(0, 0, W / 3 + 0.5, H), a);
        Fill(g, new Rect(W / 3, 0, W / 3 + 0.5, H), b);
        Fill(g, new Rect(2 * W / 3, 0, W / 3, H), c);
    });

    private static ImageSource V2(Color left, Color right, double leftFraction) => Build(g =>
    {
        Fill(g, new Rect(0, 0, W * leftFraction + 0.5, H), left);
        Fill(g, new Rect(W * leftFraction, 0, W * (1 - leftFraction), H), right);
    });

    private static ImageSource CircleOnField(Color field, Color circle) => Build(g =>
    {
        Fill(g, new Rect(0, 0, W, H), field);
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(circle), null,
            new EllipseGeometry(new Point(W / 2, H / 2), 4.6, 4.6)));
    });

    private static ImageSource UsFlag() => Build(g =>
    {
        Fill(g, new Rect(0, 0, W, H), Rgb(0xB2, 0x22, 0x34));   // red field
        for (int i = 1; i < 13; i += 2)
        {
            Fill(g, new Rect(0, i * H / 13.0, W, H / 13.0 + 0.4), White);   // white stripes
        }

        Fill(g, new Rect(0, 0, W * 0.42, H * 7 / 13.0), Rgb(0x3C, 0x3B, 0x6E));   // blue canton
    });

    private static void Fill(DrawingGroup g, Rect r, Color c)
        => g.Children.Add(new GeometryDrawing(new SolidColorBrush(c), null, new RectangleGeometry(r)));

    private static ImageSource Build(Action<DrawingGroup> draw)
    {
        var g = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, W, H), 3, 3) };
        draw(g);
        g.Children.Add(new GeometryDrawing(null,
            new Pen(new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0)), 1),
            new RectangleGeometry(new Rect(0.5, 0.5, W - 1, H - 1), 3, 3)));   // subtle border

        var image = new DrawingImage(g);
        image.Freeze();
        return image;
    }
}
