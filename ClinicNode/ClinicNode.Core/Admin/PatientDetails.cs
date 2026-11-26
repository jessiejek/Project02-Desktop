//JJQR - AI (2026-11-26) - GAV-10: owner wants the doctor to edit ALL patient details (also while consulting). One shape for the "Edit details" dialog
// (consultation header + patient chart): every field PUT /api/patients/{id} copies onto the patient (cloud PatientsController.Update).
// NOT here because that PUT ignores them: email, senior ID, PWD ID, guardian, pregnancy/OB and renal-lab fields (they are never changed by this dialog).
using System.Text.RegularExpressions;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;

namespace ClinicNode.Core.Admin;

public sealed record PatientDetails(
    string FirstName,
    string? MiddleName,
    string LastName,
    DateOnly DateOfBirth,
    SexType Sex,
    string? CivilStatus = null,
    string? ContactNumber = null,
    string? Address = null,
    string? City = null,
    string? ZipCode = null,
    string? BloodType = null,
    string? PhilhealthNumber = null,
    string? HmoProvider = null,
    string? HmoCardNumber = null,
    string? EmergencyContactName = null,
    string? EmergencyContactNumber = null,
    string? EmergencyContactRelationship = null,
    string PatientCode = "");

public static partial class PatientDetailsRules
{
    public static readonly string[] CivilStatusOptions = ["Single", "Married", "Widowed", "Separated", "Annulled"];
    public static readonly string[] BloodTypeOptions = ["A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-"];

    public static PatientDetails From(Patient p) => new(
        p.FirstName, p.MiddleName, p.LastName, p.DateOfBirth, p.Sex, p.CivilStatus, p.ContactNumber, p.Address, p.City, p.ZipCode,
        p.BloodType, p.PhilhealthNumber, p.HmoProvider, p.HmoCardNumber, p.EmergencyContactName, p.EmergencyContactNumber,
        p.EmergencyContactRelationship, p.PatientCode ?? "");

    /// <summary>Same rule as the web toTitleCase / admin dialog: "juan dela cruz" → "Juan Dela Cruz".</summary>
    public static string TitleCase(string name) =>
        WordRx().Replace(name.Trim(), m => char.ToUpperInvariant(m.Value[0]) + m.Value[1..].ToLowerInvariant());

    [GeneratedRegex(@"[^\s'-]+")]
    private static partial Regex WordRx();

    static string? Opt(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Trimmed, names title-cased, blanks → null (what is saved).</summary>
    public static PatientDetails Normalize(PatientDetails d) => d with
    {
        FirstName = TitleCase(d.FirstName ?? ""),
        MiddleName = string.IsNullOrWhiteSpace(d.MiddleName) ? null : TitleCase(d.MiddleName),
        LastName = TitleCase(d.LastName ?? ""),
        CivilStatus = Opt(d.CivilStatus), ContactNumber = Opt(d.ContactNumber), Address = Opt(d.Address), City = Opt(d.City),
        ZipCode = Opt(d.ZipCode), BloodType = Opt(d.BloodType), PhilhealthNumber = Opt(d.PhilhealthNumber),
        HmoProvider = Opt(d.HmoProvider), HmoCardNumber = Opt(d.HmoCardNumber), EmergencyContactName = Opt(d.EmergencyContactName),
        EmergencyContactNumber = Opt(d.EmergencyContactNumber), EmergencyContactRelationship = Opt(d.EmergencyContactRelationship),
    };

    /// <summary>Copies the details onto the patient; returns the changed fields as (snake_case sync name, label for the audit log).</summary>
    public static List<(string Field, string Label)> ApplyTo(Patient p, PatientDetails raw)
    {
        var d = Normalize(raw);
        var changed = new List<(string, string)>();
        void Set<T>(string field, string label, T current, T next, Action<T> write)
        {
            if (EqualityComparer<T>.Default.Equals(current, next)) return;
            write(next);
            changed.Add((field, label));
        }
        Set("first_name", "First name", p.FirstName, d.FirstName, v => p.FirstName = v);
        Set("middle_name", "Middle name", p.MiddleName, d.MiddleName, v => p.MiddleName = v);
        Set("last_name", "Last name", p.LastName, d.LastName, v => p.LastName = v);
        Set("date_of_birth", "Date of birth", p.DateOfBirth, d.DateOfBirth, v => p.DateOfBirth = v);
        Set("sex", "Sex", p.Sex, d.Sex, v => p.Sex = v);
        Set("civil_status", "Civil status", p.CivilStatus, d.CivilStatus, v => p.CivilStatus = v);
        Set("contact_number", "Contact number", p.ContactNumber, d.ContactNumber, v => p.ContactNumber = v);
        Set("address", "Address", p.Address, d.Address, v => p.Address = v);
        Set("city", "City", p.City, d.City, v => p.City = v);
        Set("zip_code", "Zip code", p.ZipCode, d.ZipCode, v => p.ZipCode = v);
        Set("blood_type", "Blood type", p.BloodType, d.BloodType, v => p.BloodType = v);
        Set("philhealth_number", "PhilHealth number", p.PhilhealthNumber, d.PhilhealthNumber, v => p.PhilhealthNumber = v);
        Set("hmo_provider", "HMO provider", p.HmoProvider, d.HmoProvider, v => p.HmoProvider = v);
        Set("hmo_card_number", "HMO card number", p.HmoCardNumber, d.HmoCardNumber, v => p.HmoCardNumber = v);
        Set("emergency_contact_name", "Emergency contact name", p.EmergencyContactName, d.EmergencyContactName, v => p.EmergencyContactName = v);
        Set("emergency_contact_number", "Emergency contact number", p.EmergencyContactNumber, d.EmergencyContactNumber, v => p.EmergencyContactNumber = v);
        Set("emergency_contact_relationship", "Emergency contact relationship", p.EmergencyContactRelationship, d.EmergencyContactRelationship, v => p.EmergencyContactRelationship = v);
        return changed;
    }

    /// <summary>The first thing the clinic server would refuse (same limits as Rules.Patient), or null.</summary>
    public static string? Problem(PatientDetails raw, DateOnly today)
    {
        var d = Normalize(raw);
        if (d.DateOfBirth > today) return "Birthday cannot be in the future.";
        if (d.DateOfBirth.Year < 1900) return "Birthday is not a valid date.";
        // the server's phone rule (Check.Phone): digits with + ( ) - . and spaces, 7–20 characters, at least 7 digits
        if (!PhoneOk(d.ContactNumber)) return "Contact number must be a phone number (digits, spaces, + - ( ) only).";
        if (!PhoneOk(d.EmergencyContactNumber)) return "Emergency contact number must be a phone number (digits, spaces, + - ( ) only).";
        var p = new Patient { Email = "" };
        ApplyTo(p, d);
        var check = AdminCheck.Patient(p);
        return check.HasErrors ? check.Errors[0] : null;
    }

    static bool PhoneOk(string? v) => v is null || (PhoneRx().IsMatch(v) && v.Count(char.IsDigit) >= 7);

    [GeneratedRegex(@"^[0-9+()\-.\s]{7,20}$")]
    private static partial Regex PhoneRx();

    /// <summary>Whole years on <paramref name="today"/> (Manila clinic day).</summary>
    public static int AgeOn(DateOnly dob, DateOnly today)
    {
        var age = today.Year - dob.Year;
        if (today.Month < dob.Month || (today.Month == dob.Month && today.Day < dob.Day)) age--;
        return Math.Max(0, age);
    }

    /// <summary>"First Last", as the doctor screens show the name.</summary>
    public static string DisplayName(PatientDetails d) => $"{d.FirstName} {d.LastName}".Trim();
}
//JJQR - END
