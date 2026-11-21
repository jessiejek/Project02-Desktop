// JJQR - AI (Nov 21, 2026) - local port of ClinicApp.Api.Validation.Rules / Check (Api is not referenced).
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;

namespace ClinicNode.Core.Admin;

internal sealed class AdminCheck
{
    private readonly List<string> _errors = [];
    public IReadOnlyList<string> Errors => _errors;
    public bool HasErrors => _errors.Count > 0;
    public string? Message => _errors.Count == 0 ? null : string.Join(" ", _errors);

    public AdminCheck Fail(string message)
    {
        _errors.Add(message);
        return this;
    }

    public AdminCheck Required(string label, string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return Fail($"{label} is required.");
        if (value.Trim().Length > max) return Fail($"{label} must be at most {max} characters.");
        return this;
    }

    public AdminCheck Optional(string label, string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return this;
        if (value.Trim().Length > max) return Fail($"{label} must be at most {max} characters.");
        return this;
    }

    public AdminCheck Email(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return this;
        try
        {
            var addr = new System.Net.Mail.MailAddress(value.Trim());
            if (!string.Equals(addr.Address, value.Trim(), StringComparison.OrdinalIgnoreCase) && addr.Address.Length == 0)
                return Fail($"{label} is not a valid email address.");
        }
        catch
        {
            return Fail($"{label} is not a valid email address.");
        }
        return this;
    }

    public AdminCheck Phone(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return this;
        var digits = value.Count(char.IsDigit);
        if (digits < 7 || digits > 15) return Fail($"{label} must be a valid phone number.");
        return this;
    }

    public AdminCheck Money(string label, decimal value)
    {
        if (value < 0) return Fail($"{label} cannot be negative.");
        return this;
    }

    public AdminCheck Range(string label, decimal value, decimal min, decimal max)
    {
        if (value < min || value > max) return Fail($"{label} must be between {min} and {max}.");
        return this;
    }

    public AdminCheck NotFuture(string label, DateOnly date)
    {
        if (date > ClinicClock.Today) return Fail($"{label} cannot be in the future.");
        return this;
    }

    public AdminCheck Hex(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return this;
        var s = value.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length is not (3 or 6) || !s.All(Uri.IsHexDigit))
            return Fail($"{label} must be a hex colour (e.g. #00685F).");
        return this;
    }

    public AdminCheck Url(string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return this;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return Fail($"{label} must be an http(s) address.");
        return this;
    }

    public static AdminCheck Patient(Patient p) => new AdminCheck()
        .Required("First name", p.FirstName, 100)
        .Optional("Middle name", p.MiddleName, 100)
        .Required("Last name", p.LastName, 100)
        .NotFuture("Date of birth", p.DateOfBirth)
        .Optional("Civil status", p.CivilStatus, 30)
        .Optional("Address", p.Address, 300)
        .Optional("City", p.City, 100)
        .Optional("Zip code", p.ZipCode, 20)
        .Phone("Contact number", p.ContactNumber)
        .Email("Email", string.IsNullOrWhiteSpace(p.Email) ? null : p.Email)
        .Optional("Emergency contact name", p.EmergencyContactName, 150)
        .Phone("Emergency contact number", p.EmergencyContactNumber)
        .Optional("Emergency contact relationship", p.EmergencyContactRelationship, 100)
        .Optional("Blood type", p.BloodType, 10)
        .Optional("PhilHealth number", p.PhilhealthNumber, 30)
        .Optional("Senior ID number", p.SeniorIdNumber, 50)
        .Optional("PWD ID number", p.PwdIdNumber, 50)
        .Optional("HMO provider", p.HmoProvider, 100)
        .Optional("HMO card number", p.HmoCardNumber, 50)
        .Optional("Guardian name", p.GuardianName, 150)
        .Optional("Guardian relationship", p.GuardianRelationship, 100);

    public static AdminCheck Announcement(string? title, string? body) => new AdminCheck()
        .Required("Title", title, 200)
        .Required("Message", body, 2000);

    public static AdminCheck Doctor(ClinicApp.Domain.Entities.Doctor d)
    {
        var c = new AdminCheck()
            .Required("Specialty", d.Specialization, 100)
            .Money("Consultation fee", d.ConsultationFee)
            .Range("Slot duration", d.SlotDurationMinutes, 5, 240)
            .Range("Slot capacity", d.SlotCapacity, 1, 50)
            .Optional("Bio", d.Bio, 2000)
            .Optional("License number", d.LicenseNumber, 50)
            .Optional("PTR number", d.PtrNumber, 50)
            .Optional("S2 number", d.S2Number, 50);
        if (d.DailyPatientLimit is { } lim)
            c.Range("Daily patient limit", lim, 1, 500);
        return c;
    }

    public static AdminCheck Staff(string? fullName, string? contact) => new AdminCheck()
        .Required("Full name", fullName, 200)
        .Phone("Contact number", contact);
}
// JJQR - END
