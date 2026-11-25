//JJQR - AI (Nov 25, 2026) - owner: "in lab orders there is an UPLOAD ... CBC, urinalysis, all that ... let it upload single or multiple files", tagged to THIS consultation.
// Lab orders step: "Upload results" in the step title bar (ConsultationPage.Ui.cs SectionTitle) and, right under the title, the files already uploaded for this visit (Open).
// Files are saved on the clinic server with this visit's booking (and consultation), so the patient chart shows them inside this visit only.
using ClinicNode.Api.Http;
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Doctor.Extra;
using ClinicNode.Core.DoctorExtra;

namespace ClinicNode.App.Views.Doctor;

public partial class ConsultationPage
{
    private List<ChartFile>? _visitFiles;
    private Guid _visitFilesFor;
    private DateOnly? _visitDate;
    private bool _uploadingResults;
    private View? _labResultsHost;
    private VerticalStackLayout? _labResultsList;
    private bool _visitFilesFailed;
    private Task? _visitFilesLoading;

    /// <summary>Owner: divide the Lab Orders step, 75% the test selection (everything RenderLabs added after <paramref name="start"/>) and 25% the uploads.</summary>
    private void SplitLabsWithUploads(int start)
    {
        var left = new VerticalStackLayout { Spacing = 8 };
        var moved = SectionHost.Children.Skip(start).ToList();
        foreach (var v in moved) SectionHost.Children.Remove(v); // Windows: detach from the old parent first, then add to the new one
        foreach (var v in moved) left.Children.Add(v);
        var split = new Grid { ColumnSpacing = 20, ColumnDefinitions = { new ColumnDefinition(new GridLength(3, GridUnitType.Star)), new ColumnDefinition(new GridLength(1, GridUnitType.Star)) } };
        split.Add(left, 0, 0);
        var right = new Border
        {
            BackgroundColor = ClinicTheme.Color("Primary").WithAlpha(0.04f), Stroke = ClinicTheme.Color("OutlineVariant"), StrokeThickness = 1, Padding = new Thickness(12),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 }, VerticalOptions = LayoutOptions.Start, Content = LabResultsBlock(),
        };
        split.Add(right, 1, 0);
        SectionHost.Children.Add(split);
    }

    private View LabResultsBlock()
    {
        // owner: the Upload button is in the step's title bar; this block (right under the title) lists what was uploaded for this consultation
        var host = new VerticalStackLayout { Spacing = 6, Margin = new Thickness(0) };
        host.Children.Add(new Label { Text = "Uploaded results", FontFamily = ClinicTheme.FontSemiBold, FontSize = 15, TextColor = ClinicTheme.Color("OnSurface") });
        var list = new VerticalStackLayout { Spacing = 6 };
        host.Children.Add(list);
        _labResultsHost = host; // the block on screen now; a load that finishes later fills only this one
        _labResultsList = list;
        //JJQR - AI (Nov 25, 2026) - code review: one load at a time per visit, and a failed load is remembered (not retried on every redraw of the step)
        if (_visitFilesFor == D.BookingId && (_visitFiles is not null || _visitFilesFailed)) FillVisitFiles(list);
        else
        {
            list.Children.Add(ClinicTheme.Text("Loading uploaded results…", "MutedSm"));
            _visitFilesLoading ??= LoadVisitFilesAsync();
        }
        //JJQR - END
        return host;
    }

    /// <summary>Forget the list so the next draw loads it again (after an upload, or back from the chart).</summary>
    private void ForgetVisitFiles() { _visitFiles = null; _visitFilesFailed = false; }

    private async Task LoadVisitFilesAsync()
    {
        var bookingId = D.BookingId;
        try
        {
            var sp = _serviceProvider;
            var doctorId = DoctorExtraUi.DoctorId(sp);
            var chart = doctorId is null ? null : await sp.GetRequiredService<IDoctorPatientsService>().GetChartAsync(doctorId.Value, D.PatientId);
            if (_workspace is null || D.BookingId != bookingId) return;
            _visitFiles = chart is null ? [] : chart.LabResults.Concat(chart.Documents).Where(f => f.BookingId == bookingId).OrderByDescending(f => f.UploadedAt).ToList();
            _visitFilesFailed = false;
            _visitFilesFor = bookingId;
            _visitDate = chart?.Bookings.FirstOrDefault(b => b.BookingId == bookingId)?.AppointmentDate;
        }
        catch (Exception ex)
        {
            ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("Consult.LabResults (load)", ex);
            _visitFiles = null; _visitFilesFailed = true; _visitFilesFor = bookingId;
        }
        finally { _visitFilesLoading = null; }
        if (_labResultsHost?.Parent is not null && _labResultsList is { } list && D.BookingId == bookingId) FillVisitFiles(list);
    }

    private void FillVisitFiles(VerticalStackLayout list)
    {
        list.Children.Clear();
        //JJQR - AI (Nov 25, 2026) - code review: a failed load says so once, with a way to try again (no automatic retry on every redraw)
        if (_visitFilesFailed)
        {
            list.Children.Add(ClinicTheme.Text("Uploaded results could not be loaded. They need the clinic server (Online mode).", "MutedSm"));
            var retry = Btn("Try again", () => { ForgetVisitFiles(); if (_section == 4) RenderSection(); }, "GhostButton");
            retry.Margin = new Thickness(0);
            list.Children.Add(new HorizontalStackLayout { Children = { retry } });
            return;
        }
        //JJQR - END
        if (_visitFiles is not { Count: > 0 } files) { list.Children.Add(ClinicTheme.Text("None yet. Press \"Upload results\" (top right) to add the patient's CBC, urinalysis, X-ray… one or several PDF, JPG or PNG files.", "MutedSm")); return; }
        foreach (var f in files)
        {
            var file = f;
            var g = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            g.Add(new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center, Children =
                { new Label { Text = file.Title, FontSize = 13, FontFamily = ClinicTheme.FontSemiBold, LineBreakMode = LineBreakMode.WordWrap },
                  new Label { Text = "Uploaded " + file.UploadedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), FontSize = 11, TextColor = ClinicTheme.Color("OnSurfaceVariant") } } }, 0, 0);
            var open = Btn("Open", () => _ = OpenVisitFileAsync(file), "GhostButton"); open.Margin = new Thickness(0); open.VerticalOptions = LayoutOptions.Center; g.Add(open, 1, 0);
            list.Children.Add(new Border
            {
                BackgroundColor = Colors.White, Stroke = ClinicTheme.Color("OutlineVariant"), StrokeThickness = 1, Padding = new Thickness(10, 6),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 }, Content = g,
            });
        }
    }

    private async Task UploadLabResultsAsync()
    {
        if (_uploadingResults || _workspace is null) return;
        _uploadingResults = true;
        try
        {
            var bookingId = D.BookingId;
            var tests = string.Join(", ", D.LabOrders.Select(l => l.TestName).Where(n => !string.IsNullOrWhiteSpace(n)));
            var (saved, problem) = await PatientFileUpload.PickAndUploadAsync(this, _serviceProvider, D.PatientId, bookingId, D.ConsultationId,
                _visitDate ?? ClinicApp.Domain.ClinicClock.Today, "lab", tests.Length > 0 ? tests : null);
            if (saved > 0)
            {
                ForgetVisitFiles(); // reload the list
                if (_section == 4 && D.BookingId == bookingId) RenderSection();
            }
            if (problem is not null) await DisplayAlertAsync("Upload results", problem, "OK");
        }
        catch (Exception ex) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("Consult.LabResults (upload)", ex); }
        finally { _uploadingResults = false; }
    }

    private async Task OpenVisitFileAsync(ChartFile file)
    {
        try
        {
            var path = await _serviceProvider.GetRequiredService<IDoctorPatientsService>().DownloadFileAsync(file);
            if (path is null) { await DisplayAlertAsync("Open file", "Files are stored on the clinic server. Connect to the internet (Online mode) to open this file.", "OK"); return; }
            await Launcher.Default.OpenAsync(new OpenFileRequest(file.Title, new ReadOnlyFile(path)));
        }
        catch (ApiNotFoundException) { await DisplayAlertAsync("Open file", "This file is no longer on the server.", "OK"); }
        catch (ApiOfflineException) { await DisplayAlertAsync("Open file", "The clinic server could not be reached. Check the internet and try again.", "OK"); }
        catch (Exception ex) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("Consult.LabResults (open)", ex); await DisplayAlertAsync("Open file", "The file could not be opened. Please try again.", "OK"); }
    }
}
//JJQR - END
