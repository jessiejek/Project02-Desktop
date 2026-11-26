// JJQR - AI (Nov 21, 2026) - Font Awesome 7 Free glyphs, same Material-Symbols names as Project02-fe/src/components/ui/Icon.tsx.
namespace ClinicNode.App.Controls;

/// <summary>Look up a web <c>Icon name</c> (e.g. "dashboard", "logout") as a Font Awesome glyph.</summary>
public readonly record struct ClinicIconGlyph(string FontFamily, string Glyph);

public static class ClinicIcons
{
    public const string SolidFamily = "FASolid";
    public const string RegularFamily = "FARegular";
    /// <summary>Sidebar and button icon size — web <c>h-4 w-4</c>.</summary>
    public const double SizeSm = 16;
    /// <summary>Status-pill icon size — web <c>text-[14px]</c>.</summary>
    public const double SizePill = 14;

    static readonly Dictionary<string, ClinicIconGlyph> Map = new(StringComparer.Ordinal)
    {
        ["menu"] = new("FASolid", "\uF0C9"),
        ["notifications"] = new("FASolid", "\uF0F3"),
        ["search"] = new("FASolid", "\uF002"),
        ["logout"] = new("FASolid", "\uF2F5"),
        ["chevron_left"] = new("FASolid", "\uF053"),
        ["chevron_right"] = new("FASolid", "\uF054"),
        ["expand_less"] = new("FASolid", "\uF077"),
        ["expand_more"] = new("FASolid", "\uF078"),
        ["calendar_today"] = new("FASolid", "\uF783"),
        ["calendar_month"] = new("FASolid", "\uF073"),
        ["event_available"] = new("FASolid", "\uF274"),
        ["event_note"] = new("FASolid", "\uF783"),
        ["event"] = new("FASolid", "\uF783"),
        ["event_busy"] = new("FASolid", "\uF273"),
        ["close"] = new("FASolid", "\uF00D"),
        ["delete"] = new("FASolid", "\uF1F8"),
        ["lock"] = new("FASolid", "\uF023"),
        ["call"] = new("FASolid", "\uF095"),
        ["mail"] = new("FASolid", "\uF0E0"),
        ["check"] = new("FASolid", "\uF00C"),
        ["check_circle"] = new("FASolid", "\uF058"),
        ["help"] = new("FASolid", "\uF059"),
        ["download"] = new("FASolid", "\uF019"),
        ["science"] = new("FASolid", "\uF0C3"),
        ["description"] = new("FASolid", "\uF15C"),
        ["person"] = new("FASolid", "\uF007"),
        ["account_circle"] = new("FASolid", "\uF2BD"),
        ["star"] = new("FASolid", "\uF005"),
        ["add_circle"] = new("FASolid", "\uF055"),
        ["add"] = new("FASolid", "\u002B"),
        ["arrow_forward"] = new("FASolid", "\uF061"),
        ["gavel"] = new("FASolid", "\uF0E3"),
        ["folder_shared"] = new("FASolid", "\uF07C"),
        ["folder_open"] = new("FASolid", "\uF07C"),
        ["prescriptions"] = new("FASolid", "\uF486"),
        ["medication"] = new("FASolid", "\uF484"),
        ["schedule"] = new("FASolid", "\uF017"),
        ["directions_walk"] = new("FASolid", "\uF554"),
        ["warning"] = new("FASolid", "\uF071"),
        ["incomplete_circle"] = new("FASolid", "\uF042"),
        ["radio_button_unchecked"] = new("FARegular", "\uF111"),
        ["analytics"] = new("FASolid", "\uE473"),
        ["monitoring"] = new("FASolid", "\uF201"),
        ["cancel"] = new("FASolid", "\uF057"),
        ["error"] = new("FASolid", "\uF06A"),
        ["info"] = new("FASolid", "\uF05A"),
        ["dashboard"] = new("FASolid", "\uF625"),
        ["medical_services"] = new("FASolid", "\uF469"),
        ["assignment"] = new("FASolid", "\uF328"),
        ["vaccines"] = new("FASolid", "\uF48E"),
        ["payments"] = new("FASolid", "\uF53A"),
        ["group"] = new("FASolid", "\uF500"),
        ["groups"] = new("FASolid", "\uF500"),
        ["campaign"] = new("FASolid", "\uF0A1"),
        ["manage_accounts"] = new("FASolid", "\uF4FE"),
        ["clinical_notes"] = new("FASolid", "\uF481"),
        ["badge"] = new("FASolid", "\uF2C1"),
        ["settings"] = new("FASolid", "\uF013"),
        ["history"] = new("FASolid", "\uF1DA"),
        ["pending_actions"] = new("FASolid", "\uF0AE"),
        ["hourglass_top"] = new("FASolid", "\uF252"),
        ["receipt_long"] = new("FASolid", "\uF543"),
        ["follow_the_signs"] = new("FASolid", "\uF277"),
        ["thermometer"] = new("FASolid", "\uF2C9"),
        ["heart_pulse"] = new("FASolid", "\uF21E"),
        ["lungs"] = new("FASolid", "\uF604"),
        ["gauge"] = new("FASolid", "\uF624"),
        ["wind"] = new("FASolid", "\uF72E"),
        ["ruler"] = new("FASolid", "\uF545"),
        ["weight_scale"] = new("FASolid", "\uF496"),
        ["view_list"] = new("FASolid", "\uF03A"),
        ["table_rows"] = new("FASolid", "\uF00B"),
        ["print"] = new("FASolid", "\uF02F"),
        ["copy"] = new("FASolid", "\uF0C5"),
        ["edit"] = new("FASolid", "\uF303"),
        ["person_off"] = new("FASolid", "\uF235"), //JJQR - AI (2026-11-26) - GAV-11: "Wrong patient?" (web faUserXmark) //JJQR - END
        ["merge"] = new("FASolid", "\uF387"), //JJQR - AI (2026-11-26) - GAV-12: "Merge to existing patient" (web faCodeMerge) //JJQR - END
        ["upload_file"] = new("FASolid", "\uF093"),
        ["devices"] = new("FASolid", "\uF109"),
        ["cloud_upload"] = new("FASolid", "\uF0EE"),
        ["eye"] = new("FASolid", "\uF06E"),
        ["eye_slash"] = new("FASolid", "\uF070"),
    };

    public static IReadOnlyCollection<string> Names => Map.Keys;

    public static bool TryGet(string name, out ClinicIconGlyph glyph) => Map.TryGetValue(name, out glyph);

    public static FontImageSource? FontImage(string name, double size, Color color)
    {
        if (!TryGet(name, out var glyph)) return null;
        return new FontImageSource
        {
            FontFamily = glyph.FontFamily,
            Glyph = glyph.Glyph,
            Size = size,
            Color = color,
        };
    }
}

/// <summary>Renders a web icon name as a 16px (default) Font Awesome image. Use in XAML: <c>&lt;ctrl:ClinicIcon Name="dashboard" /&gt;</c>.</summary>
public sealed class ClinicIcon : Image
{
    public static readonly BindableProperty NameProperty =
        BindableProperty.Create(nameof(Name), typeof(string), typeof(ClinicIcon), propertyChanged: OnChanged);
    public static readonly BindableProperty IconColorProperty =
        BindableProperty.Create(nameof(IconColor), typeof(Color), typeof(ClinicIcon), propertyChanged: OnChanged);
    public static readonly BindableProperty IconSizeProperty =
        BindableProperty.Create(nameof(IconSize), typeof(double), typeof(ClinicIcon), ClinicIcons.SizeSm, propertyChanged: OnChanged);

    public string? Name
    {
        get => (string?)GetValue(NameProperty);
        set => SetValue(NameProperty, value);
    }

    public Color? IconColor
    {
        get => (Color?)GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public ClinicIcon()
    {
        Aspect = Aspect.AspectFit;
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.Center;
        WidthRequest = ClinicIcons.SizeSm;
        HeightRequest = ClinicIcons.SizeSm;
    }

    static void OnChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is ClinicIcon icon) icon.Refresh();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        Refresh();
    }

    void Refresh()
    {
        var size = IconSize > 0 ? IconSize : ClinicIcons.SizeSm;
        WidthRequest = size;
        HeightRequest = size;
        var color = IconColor ?? ClinicTheme.Color("OnSurfaceVariant");
        Source = string.IsNullOrWhiteSpace(Name) ? null : ClinicIcons.FontImage(Name, size, color);
    }
}
// JJQR - END
