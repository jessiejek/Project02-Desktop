//JJQR - AI (Nov 22, 2026) - owner: every password box needs the eye (show / hide), like the sign-in page. One shared wrapper; hidden by default; hides again when the page is left.
namespace ClinicNode.App.Controls;

public static class PasswordEye
{
    /// <summary>Returns the entry with an eye button at its right edge. The entry itself stays the same object, so existing code that reads <c>entry.Text</c> keeps working.</summary>
    public static View Wrap(Entry entry)
    {
        var eye = new ImageButton
        {
            BackgroundColor = Colors.Transparent, WidthRequest = 40, HeightRequest = 40, Padding = new Thickness(8),
            HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center,
        };
        void Glyph()
        {
            var hidden = entry.IsPassword;
            eye.Source = ClinicIcons.FontImage(hidden ? "eye" : "eye_slash", ClinicIcons.SizeSm, ClinicTheme.Color("OnSurfaceVariant"));
            SemanticProperties.SetDescription(eye, hidden ? "Show password" : "Hide password");
        }
        eye.Clicked += (_, _) => { entry.IsPassword = !entry.IsPassword; Glyph(); };
        entry.Unloaded += (_, _) => { entry.IsPassword = true; Glyph(); };   // hidden again when the page is left
        Glyph();
        var grid = new Grid { Children = { entry, eye } };
        entry.Margin = new Thickness(0, 0, 44, 0);                            // the text never runs under the eye
        return grid;
    }
}
//JJQR - END
