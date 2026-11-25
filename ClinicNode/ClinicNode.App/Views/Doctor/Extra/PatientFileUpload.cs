//JJQR - AI (Nov 25, 2026) - owner: upload a patient's results (CBC, urinalysis, X-ray... one or several files: PDF, JPG, PNG, up to 10 MB each) and TAG them to one
// consultation (its booking). Used by the consultation's Lab orders step and by each visit in the patient chart. Needs the clinic server (Online mode).
using ClinicNode.Api.Http;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.DoctorExtra;

namespace ClinicNode.App.Views.Doctor.Extra;

public static class PatientFileUpload
{
    private const int MaxBytes = 10 * 1024 * 1024;
    private const int MaxFiles = 10; //JJQR - AI (Nov 25, 2026) - code review: at most 10 files per upload (each up to 10 MB is held in memory) //JJQR - END
    private static readonly FilePickerFileType Types = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.WinUI] = [".pdf", ".jpg", ".jpeg", ".png"],
        [DevicePlatform.MacCatalyst] = ["com.adobe.pdf", "public.jpeg", "public.png"],
    });

    /// <summary>Pick one or more files, ask for a title, upload each one to the given visit. Returns how many were saved, or a message for the screen.</summary>
    public static async Task<(int Saved, string? Problem)> PickAndUploadAsync(Page from, IServiceProvider sp, Guid patientId, Guid bookingId, Guid? consultationId,
        DateOnly visitDate, string kind = "lab", string? suggestedTitle = null)
    {
        IEnumerable<FileResult?>? picked;
        try { picked = await FilePicker.Default.PickMultipleAsync(new PickOptions { PickerTitle = "Choose one or more PDF, JPG or PNG files", FileTypes = Types }); }
        catch (Exception ex) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("Upload (pick)", ex); return (0, "The file picker could not be opened. Please try again."); }
        var files = (picked ?? []).Where(f => f is not null).Cast<FileResult>().ToList();
        if (files.Count == 0) return (0, null);
        if (files.Count > MaxFiles) return (0, $"Choose up to {MaxFiles} files at a time (you chose {files.Count}). Nothing was uploaded."); //JJQR - AI (Nov 25, 2026) - code review: every file is read into memory first, so cap how many //JJQR - END

        var ready = new List<(string Name, string Type, byte[] Bytes)>();
        foreach (var f in files)
        {
            var type = Path.GetExtension(f.FileName).ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf", ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", _ => null,
            };
            if (type is null) return (0, $"\"{f.FileName}\" is not a PDF, JPG or PNG file. Nothing was uploaded.");
            byte[] bytes;
            try
            {
                await using var s = await f.OpenReadAsync();
                using var ms = new MemoryStream();
                await s.CopyToAsync(ms);
                bytes = ms.ToArray();
            }
            catch (Exception ex) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("Upload (read)", ex); return (0, $"\"{f.FileName}\" could not be read. Nothing was uploaded."); }
            if (bytes.Length == 0) return (0, $"\"{f.FileName}\" is empty. Nothing was uploaded.");
            if (bytes.Length > MaxBytes) return (0, $"\"{f.FileName}\" is larger than 10 MB. Scan or save it smaller, then try again. Nothing was uploaded.");
            ready.Add((f.FileName, type, bytes));
        }

        var lab = kind == "lab";
        var names = string.Join("\n", ready.Select(r => "• " + r.Name));
        var saved = 0;
        //JJQR - AI (2026-11-25) - owner: several files with no suggested title opened with an empty Title; pre-fill "Lab results <visit date>" / "Documents <visit date>" (still editable; "(1 of 3)" is still added per file). Same as the web (visit-upload.ts defaultUploadTitle).
        var suggested = string.IsNullOrWhiteSpace(suggestedTitle) ? null : suggestedTitle.Trim();
        var title0 = ready.Count == 1
            ? suggested ?? Path.GetFileNameWithoutExtension(ready[0].Name)
            : suggested ?? $"{(lab ? "Lab results" : "Documents")} {visitDate:yyyy-MM-dd}";
        //JJQR - END
        var ok = await StaffDialog.ShowAsync(from, ready.Count == 1 ? (lab ? "Upload lab result" : "Upload document") : $"Upload {ready.Count} files",
            $"{names}\n\nSaved with the consultation on {visitDate:yyyy-MM-dd}.",
            "Title", lab ? "e.g. CBC, Urinalysis, Chest X-ray" : "e.g. Referral letter", "Cancel", "Upload",
            async title =>
            {
                if (string.IsNullOrWhiteSpace(title)) return "Enter a title.";
                var svc = sp.GetRequiredService<IDoctorPatientsService>();
                // a retry after a partial failure skips the files already saved
                for (var i = saved; i < ready.Count; i++)
                {
                    var r = ready[i];
                    var name = ready.Count == 1 ? title.Trim() : $"{title.Trim()} ({i + 1} of {ready.Count})";
                    try
                    {
                        if (!await svc.UploadFileAsync(patientId, bookingId, consultationId, kind, name, r.Name, r.Type, r.Bytes))
                            return "Files are stored on the clinic server. Connect to the internet (Online mode) to upload.";
                        saved = i + 1;
                    }
                    catch (ApiValidationException ex) { return Partial(saved, $"\"{r.Name}\": {ex.Message}"); }
                    catch (ApiForbiddenException) { return Partial(saved, "You do not have permission to add files to this patient."); }
                    catch (ApiOfflineException) { return Partial(saved, $"The connection dropped while sending \"{r.Name}\". It may already be saved: press Cancel and check the list before uploading it again."); } //JJQR - AI (Nov 25, 2026) - code review: a timeout can still be saved on the server, so warn before a retry makes a duplicate //JJQR - END
                    catch (Exception ex) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("Upload", ex); return Partial(saved, $"\"{r.Name}\" could not be uploaded. Please try again."); }
                }
                return null;
            },
            multiline: false, initialText: title0, maxLength: 180);
        return (saved, !ok && saved > 0 ? $"{saved} of {ready.Count} files were uploaded before you cancelled." : null);
    }

    private static string Partial(int saved, string why) => saved == 0 ? why : $"{saved} file(s) saved. {why} Press Upload to send the rest.";
}
//JJQR - END
