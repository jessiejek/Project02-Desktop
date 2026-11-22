//JJQR - AI (Nov 21, 2026) - state + actions of the web WalkInWizard (Patient → Confirm → done). Shared by staff-walk-in and (allen4) admin-walk-in. Never starts a consultation.
//JJQR - AI (21 Nov 2026) - 1:1 pass against components/walkin/WalkInWizard.tsx: picking a row only selects it (radio), "Continue" goes to Confirm,
// Back keeps the selection; Senior is auto-set only when the picked patient changes; a failed first load is a "Patients unavailable" state with
// Try again; check-in failure is the web's fixed message; quick register selects the new patient and puts it first in the list.
// Routing unchanged: IStaffWalkInService (POST /api/queue with visit_type / discount_category) when registered, else the node queue.
//JJQR - END
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Queue;
using ClinicNode.Core.Staff;

namespace ClinicNode.App.ViewModels.Staff;

public enum WalkInStep { Patient, Confirm, Done }

public sealed class WalkInWizardViewModel
{
    public const int PageSize = 10;
    //JJQR - AI (21 Nov 2026) - web handleCheckIn catch message
    public const string CheckInFailed = "Could not check this patient in. Please try again.";
    public const string LoadFailedTitle = "Patients unavailable";
    public const string LoadFailedMessage = "The patient list could not be loaded, so check-in can't start yet. Try again.";
    //JJQR - END
    private readonly IStaffPatientsService _patients;
    private readonly IQueueService _queue;
    private int _searchVersion;
    //JJQR - AI (21 Nov 2026) - online-first walk-in: POST /api/queue through IStaffWalkInService when registered (StaffModule), else the node queue as before.
    private readonly IStaffWalkInService? _walkIn;

    public WalkInWizardViewModel(IStaffPatientsService patients, IQueueService queue, IStaffWalkInService? walkIn = null)
    {
        _patients = patients;
        _queue = queue;
        _walkIn = walkIn;
    }
    //JJQR - END

    /// <summary>Raised whenever the view should re-render.</summary>
    public event Action? Changed;

    public WalkInStep Step { get; private set; } = WalkInStep.Patient;
    public string Query { get; private set; } = "";
    public int Page { get; private set; } = 1;
    public PatientPage? Results { get; private set; }
    public StaffPatientRow? Patient { get; private set; }
    public VisitType VisitType { get; set; } = VisitType.New;
    /// <summary>null = None, "Senior", "PWD" (web discount values).</summary>
    public string? Discount { get; set; }
    public bool MedCertRequested { get; set; }
    public string? Error { get; private set; }
    public bool Busy { get; private set; }
    public WalkInTicket? Ticket { get; private set; }
    //JJQR - AI (21 Nov 2026) - web loading / loadError (first load only; later searches keep the list and show Error)
    public bool Loading { get; private set; } = true;
    public bool LoadFailed { get; private set; }
    //JJQR - END

    public bool PatientIsSenior => Patient is { } p && StaffRules.IsSeniorCitizen(p.DateOfBirth, ClinicClock.Today);
    public int PatientAge => Patient is { } p ? StaffRules.AgeOn(p.DateOfBirth, ClinicClock.Today) : 0;

    //JJQR - AI (21 Nov 2026) - the two web hints under Discount
    /// <summary>"Set automatically: patient is 60 or older." (Senior chosen and the patient is 60+), else null.</summary>
    public string? DiscountAutoHint => Discount == "Senior" && PatientIsSenior ? "Set automatically: patient is 60 or older." : null;

    /// <summary>Priority-lane hint: the server also gives a 60+ patient the S- number with "None".</summary>
    public string DiscountLaneHint => Discount is not null || PatientIsSenior
        ? "Gets a priority (S-) queue number, served before the regular line."
        : "Senior citizens and PWD get a priority (S-) queue number.";

    /// <summary>Kept for existing callers: both hints in one line.</summary>
    public string DiscountHint => (DiscountAutoHint is { } a ? a + " " : "") + DiscountLaneHint;
    //JJQR - END

    public async Task SearchAsync(string? query, int page = 1)
    {
        Query = query?.Trim() ?? "";
        Page = Math.Max(1, page);
        var v = ++_searchVersion;
        try
        {
            var res = await _patients.SearchAsync(Query, Page, PageSize);
            if (v != _searchVersion) return; // a newer search won
            Results = res;
            Error = null;
            LoadFailed = false; //JJQR - AI (21 Nov 2026) //JJQR - END
        }
        //JJQR - AI (21 Nov 2026) - web: a failed load is the "Patients unavailable" state (no exception text)
        catch (Exception)
        {
            if (v != _searchVersion) return;
            if (Results is null) LoadFailed = true;
            else Error = LoadFailedMessage;
        }
        Loading = false;
        //JJQR - END
        Changed?.Invoke();
    }

    //JJQR - AI (21 Nov 2026) - web ErrorState "Try again"
    public Task RetryLoadAsync()
    {
        Loading = true;
        LoadFailed = false;
        Changed?.Invoke();
        return SearchAsync(Query, Page);
    }
    //JJQR - END

    public Task NextPageAsync() => Results is { } r && Page < r.PageCount ? SearchAsync(Query, Page + 1) : Task.CompletedTask;
    public Task PrevPageAsync() => Page > 1 ? SearchAsync(Query, Page - 1) : Task.CompletedTask;

    //JJQR - AI (21 Nov 2026) - web setPatientId: select the row (radio) on step 1; Senior is re-derived only when the patient changes.
    public void Pick(StaffPatientRow patient)
    {
        if (Patient?.PatientId != patient.PatientId)
            Discount = StaffRules.DefaultDiscountFor(patient.DateOfBirth, ClinicClock.Today); // RA 9994: auto Senior at 60+
        Patient = patient;
        Error = null;
        Changed?.Invoke();
    }

    /// <summary>Sticky bar "Continue" (enabled once a patient is selected).</summary>
    public void Continue()
    {
        if (Patient is null) return;
        _searchVersion++; // ignore any search still in flight
        Error = null;
        Step = WalkInStep.Confirm;
        Changed?.Invoke();
    }

    /// <summary>Quick register "Register and select": the new patient goes to the top of the list and is selected (web setPatients([created, ...prev])).</summary>
    public void Registered(StaffPatientRow created)
    {
        if (Results is { } r)
            Results = r with { Items = [created, .. r.Items.Where(i => i.PatientId != created.PatientId)], TotalCount = r.TotalCount + 1 };
        Pick(created);
    }
    //JJQR - END

    /// <summary>Pick + Continue in one call (kept for existing callers).</summary>
    public void Select(StaffPatientRow patient)
    {
        Pick(patient); //JJQR - AI (21 Nov 2026) //JJQR - END
        Continue();
    }

    public void Back()
    {
        //JJQR - AI (21 Nov 2026) - web setStep(1): the selection, visit type, discount and med-cert choice stay
        Error = null;
        Step = WalkInStep.Patient;
        //JJQR - END
        Changed?.Invoke();
    }

    //JJQR - AI (Nov 22, 2026) - step 0.3
    Guid _attemptPatientId;
    Guid _attemptBookingId;
    Guid _attemptPaymentId;
    //JJQR - END

    /// <summary>"Check in and get queue number": issues the R-/S- ticket on this node. Does NOT open a consult.</summary>
    public async Task<bool> CheckInAsync()
    {
        if (Patient is null || Busy) return false;
        Busy = true;
        Error = null;
        Changed?.Invoke(); //JJQR - AI (21 Nov 2026) "Checking in…" //JJQR - END
        try
        {
            //JJQR - AI (21 Nov 2026) - online-first walk-in check-in
            //JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.3: the booking and payment ids are made ONCE per check-in attempt and reused if the button is pressed again, so a lost reply / retry returns the same booking
            if (_attemptPatientId != Patient.PatientId) { _attemptPatientId = Patient.PatientId; _attemptBookingId = Guid.NewGuid(); _attemptPaymentId = Guid.NewGuid(); }
            var request = new WalkInRequest(Patient.PatientId, null, VisitType, MedCertRequested, Discount, null, _attemptBookingId, _attemptPaymentId);
            //JJQR - END
            var r = _walkIn is not null ? await _walkIn.CheckInWalkInAsync(request) : await _queue.CheckInWalkInAsync(request);
            //JJQR - END
            if (!r.Succeeded || r.Ticket is null)
            {
                Error = CheckInFailed; //JJQR - AI (21 Nov 2026) web: one fixed message //JJQR - END
                return false;
            }
            Ticket = r.Ticket;
            _attemptPatientId = Guid.Empty; //JJQR - AI (Nov 22, 2026) - step 0.3: the next walk-in gets new ids //JJQR - END
            Step = WalkInStep.Done;
            return true;
        }
        //JJQR - AI (21 Nov 2026)
        catch (Exception)
        {
            Error = CheckInFailed;
            return false;
        }
        //JJQR - END
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>"Next walk-in": back to an empty step 1 (web resetForNext).</summary>
    public async Task ResetAsync()
    {
        Patient = null;
        Ticket = null;
        Error = null;
        //JJQR - AI (21 Nov 2026) - web resetForNext also resets visit type, discount and med cert
        VisitType = VisitType.New;
        Discount = null;
        MedCertRequested = false;
        //JJQR - END
        Step = WalkInStep.Patient;
        await SearchAsync("", 1);
    }
}
//JJQR - END
