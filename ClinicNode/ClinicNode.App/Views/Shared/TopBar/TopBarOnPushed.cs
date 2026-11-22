//JJQR - AI (Nov 22, 2026) - owner: pages opened over another page (a visit, a chart, a consultation) must show the top bar with the connection ping.
// IMPORTANT (lesson from the crash the same day): the top bar is attached in the page's CONSTRUCTOR, before the page is ever shown. Setting it after the page has been displayed
// (what the first version did in the shell's Navigated event) crashed MAUI navigation on Mac Catalyst ("Failed to marshal the Objective-C object ... TitleViewContainer").
namespace ClinicNode.App.Views.Shared;

public static class TopBarOnPushed
{
    public static void Attach(Page page, IServiceProvider sp)
    {
        try { Shell.SetTitleView(page, new TopBarView(sp)); }
        catch { /* the top bar must never stop a page from opening */ }
    }
}
//JJQR - END
