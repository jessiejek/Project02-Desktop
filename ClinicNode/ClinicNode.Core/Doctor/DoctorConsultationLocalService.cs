//JJQR - AI (21 Nov 2026) - offline (local SQLite) consultation service for the online-first router. Wraps the existing
// Core/Clinical ConsultationService (not edited) and adds the web consultation2 rules it does not have yet:
//   • waive reason needs ≥ 5 characters; a Charge with no typed amount follows the computed total (web pfAmountEdited)
//   • an AMENDMENT may change the fee decision; a Waive on a finalised visit auto-waives an unpaid payment (ConsultationsController)
//   • vaccination route / site / lot / expiry / manufacturer are stored (the shared draft model has no fields for them)
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Clinical;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Doctor;

public sealed class DoctorConsultationLocalService : IConsultationService
{
    private readonly ConsultationService _inner;
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly ISyncOutbox _outbox;
    private readonly IAuthService _auth;

    public DoctorConsultationLocalService(ConsultationService inner, IDbContextFactory<ClinicAppDbContext> factory, ISyncOutbox outbox, IAuthService auth)
    {
        _inner = inner;
        _factory = factory;
        _outbox = outbox;
        _auth = auth;
    }

    public IReadOnlyList<string> MissingRequired(ConsultationDraft draft)
    {
        // A Charge without a typed amount is fine: the computed total is used.
        decimal? shown = draft.PfDecision == "Charge" && draft.PfAmount is null && !draft.FeeState().PfAmountEdited ? 1m : draft.PfAmount;
        return ConsultRules.MissingRequired(draft, shown).Select(i => ConsultSteps.Sections[i]).ToList();
    }

    public async Task<ConsultationWorkspace> LoadByBookingAsync(Guid bookingId, CancellationToken ct = default)
    {
        var ws = await _inner.LoadByBookingAsync(bookingId, ct);
        var d = ws.Draft;
        // A stored amount is the doctor's own (web); the old local save stored the computed total, so an equal amount still follows the total.
        d.FeeState().PfAmountEdited = d.PfDecision == "Charge" && d.PfAmount is { } a && a != ws.ComputedFee;
        if (!d.FeeState().PfAmountEdited && d.PfDecision == "Charge") d.PfAmount = null;
        if (d.ConsultationId is { } cid && d.Vaccinations.Count > 0)
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            var rows = await db.PatientVaccinations.AsNoTracking().Where(v => v.ConsultationId == cid).ToListAsync(ct);
            foreach (var line in d.Vaccinations)
                if (rows.FirstOrDefault(r => r.Id == line.Id) is { } r)
                {
                    var x = line.Details();
                    x.Manufacturer = r.Manufacturer; x.Route = r.Route; x.Site = r.Site; x.LotNumber = r.LotNumber; x.ExpiryDate = r.ExpiryDate;
                }
        }
        return ws;
    }

    public async Task<ConsultSaveResult> SaveDraftAsync(ConsultationDraft draft, CancellationToken ct = default)
    {
        var edited = draft.FeeState().PfAmountEdited;
        var typed = draft.PfAmount;
        //JJQR - AI (21 Nov 2026) - vaccine rows: keep next_dose_date / administered_date / reaction / status across the local replace-all
        var vaxKeep = await SnapshotVaccinationsAsync(draft, ct);
        //JJQR - END
        var r = await _inner.SaveDraftAsync(draft, ct);
        if (r.Succeeded) await AfterSaveAsync(draft, r, ct, vaxKeep);
        draft.PfAmount = edited ? typed : null; // keep following the total in the editor
        return r;
    }

    public async Task<ConsultSaveResult> CompleteAsync(ConsultationDraft draft, CancellationToken ct = default)
    {
        if (draft.PfDecision == "Waive" && (draft.PfWaiveReason ?? "").Trim().Length < ConsultRules.WaiveReasonMinLength)
            return ConsultSaveResult.Fail("Waive needs a reason of at least 5 characters.", [ConsultSteps.Sections[ConsultSteps.FeeSectionIndex]]);
        if (draft.PfDecision == "Charge" && draft.PfAmount is null)
            draft.PfAmount = await ComputedTotalAsync(draft, ct);
        //JJQR - AI (21 Nov 2026)
        var vaxKeep = await SnapshotVaccinationsAsync(draft, ct);
        //JJQR - END
        var r = await _inner.CompleteAsync(draft, ct);
        if (r.Succeeded) await AfterSaveAsync(draft, r, ct, vaxKeep);
        return r;
    }

    public async Task<ConsultSaveResult> AmendAsync(ConsultationDraft draft, CancellationToken ct = default)
    {
        if (draft.PfDecision == "Waive" && (draft.PfWaiveReason ?? "").Trim().Length < ConsultRules.WaiveReasonMinLength)
            return ConsultSaveResult.Fail("Waive needs a reason of at least 5 characters.", [ConsultSteps.Sections[ConsultSteps.FeeSectionIndex]]);
        var wanted = (Decision: draft.PfDecision, Amount: draft.PfAmount, Reason: draft.PfWaiveReason, Edited: draft.FeeState().PfAmountEdited);
        //JJQR - AI (Nov 23, 2026) - offline audit: amending a completed visit whose fee follows the total failed with "Finish Professional Fee Decision to save the amendment". The editor keeps such a fee as empty ("follows the total") and the inner amend refuses an empty Charge amount.
        // Complete already fills the amount in first; do the same here, then give the editor back its "follows the total" state.
        if (wanted.Decision == "Charge" && wanted.Amount is null) { wanted.Amount = await ComputedTotalAsync(draft, ct); draft.PfAmount = wanted.Amount; }
        //JJQR - END
        //JJQR - AI (21 Nov 2026)
        var vaxKeep = await SnapshotVaccinationsAsync(draft, ct);
        //JJQR - END
        var r = await _inner.AmendAsync(draft, ct); // keeps the stored fee; we apply the doctor's decision below
        if (wanted.Decision == "Charge" && !wanted.Edited) draft.PfAmount = null; //JJQR - AI (Nov 23, 2026) - keep following the total in the editor //JJQR - END
        if (!r.Succeeded) return r;
        var fail = await ApplyAmendedFeeAsync(draft, wanted.Decision, wanted.Amount, wanted.Reason, ct);
        if (fail is not null) return ConsultSaveResult.Fail(fail);
        await AfterSaveAsync(draft, r, ct, vaxKeep);
        return r;
    }

    private async Task<decimal> ComputedTotalAsync(ConsultationDraft d, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var s = await db.ClinicSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct) ?? new ClinicSetting();
        return FeeCalculator.Compute(s, d.VisitType, d.DiscountCategory, d.MedCertRequested).Total;
    }

    /// <summary>Mirror of ConsultationsController's amend rules: a settled payment is never contradicted; an unpaid one follows the decision.</summary>
    private async Task<string?> ApplyAmendedFeeAsync(ConsultationDraft draft, string? decision, decimal? amount, string? reason, CancellationToken ct)
    {
        if (decision is not ("Charge" or "Waive")) return null;
        await using var db = await _factory.CreateDbContextAsync(ct);
        var consult = await db.Consultations.SingleOrDefaultAsync(c => c.BookingId == draft.BookingId, ct);
        if (consult is null) return null;
        var pay = await db.Payments.SingleOrDefaultAsync(p => p.BookingId == draft.BookingId, ct);
        var booking = await db.Bookings.SingleOrDefaultAsync(b => b.BookingId == draft.BookingId, ct);
        if (consult.PfDecision is not null && pay is not null && pay.Status != PaymentStatus.Unpaid && consult.PfDecision != decision)
        {
            if (pay.Status == PaymentStatus.Waived && decision == "Charge")
                return "This visit's professional fee was already waived, so it can't be changed to Charge. Ask an admin if the patient should be billed.";
            if (pay.Status is PaymentStatus.Paid or PaymentStatus.Refunded && decision == "Waive")
                return "This visit's professional fee was already paid, so it can't be waived. Refund the payment first.";
        }
        if (pay is { Status: PaymentStatus.Paid } && decision == "Charge" && amount is { } restated && restated != pay.Amount)
            return $"This visit's professional fee was already paid (₱{pay.Amount:0.##}), so the amount can't be changed to ₱{restated:0.##}.";

        var now = DateTimeOffset.UtcNow;
        consult.PfDecision = decision;
        consult.PfAmount = decision == "Charge" ? amount : null;
        consult.PfWaiveReason = decision == "Waive" ? reason?.Trim() : null;
        consult.UpdatedAt = now;
        var paymentTouched = false;
        if (pay is { Status: PaymentStatus.Unpaid })
        {
            if (decision == "Waive" && !string.IsNullOrWhiteSpace(consult.PfWaiveReason))
            {
                pay.Status = PaymentStatus.Waived;
                pay.WaivedReason = consult.PfWaiveReason;
                pay.WaivedByUserId = _auth.Current?.UserId;
                pay.WaivedAt = now;
                if (booking is not null) { booking.AmountDue = 0; booking.UpdatedAt = now; }
            }
            else if (decision == "Charge" && amount is { } a)
            {
                pay.Amount = a;
                if (booking is not null) { booking.TotalFee = a; booking.AmountDue = a; booking.UpdatedAt = now; }
            }
            pay.UpdatedAt = now;
            paymentTouched = true;
        }
        if (paymentTouched)
            await _outbox.EnqueueAsync(db, new SyncOutboxEntry
            {
                OperationId = Guid.NewGuid(), Aggregate = "payment", EntityId = pay!.PaymentId, OperationType = "upsert",
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { paymentId = pay.PaymentId, bookingId = draft.BookingId }), CreatedAt = now,
            }, ct);
        await db.SaveChangesAsync(ct);
        draft.PfDecision = consult.PfDecision; draft.PfAmount = consult.PfAmount; draft.PfWaiveReason = consult.PfWaiveReason;
        return null;
    }

    /// <summary>Store the vaccination extras on the rows ConsultationService just wrote (matched by name + dose, in order).</summary>
    //JJQR - AI (21 Nov 2026) - vaccine NextDoseDate fix: ConsultationService replaces this visit's rows (new ids keep, but next_dose_date,
    // administered_date, reaction_notes and status were reset). Remember them per row id before the save and put them back after.
    private sealed record VaxKeep(DateOnly? NextDoseDate, DateOnly? AdministeredDate, string? ReactionNotes, VaccinationStatus Status);

    private async Task<Dictionary<Guid, VaxKeep>> SnapshotVaccinationsAsync(ConsultationDraft d, CancellationToken ct)
    {
        // new staged lines get their id now, so the same row id survives every later save (and the cloud push)
        foreach (var line in d.Vaccinations) line.Id ??= Guid.NewGuid();
        if (d.ConsultationId is not { } cid) return new();
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.PatientVaccinations.AsNoTracking().Where(v => v.ConsultationId == cid)
            .ToDictionaryAsync(v => v.Id, v => new VaxKeep(v.NextDoseDate, v.AdministeredDate, v.ReactionNotes, v.Status), ct);
    }
    //JJQR - END

    private async Task AfterSaveAsync(ConsultationDraft sent, ConsultSaveResult r, CancellationToken ct, Dictionary<Guid, VaxKeep>? keep = null)
    {
        if (r.Draft is { } back) ConsultDraftExtras.CopyExtras(sent, back);
        var d = r.Draft ?? sent;
        if (d.ConsultationId is not { } cid || d.Vaccinations.Count == 0) return;
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.PatientVaccinations.Where(v => v.ConsultationId == cid).ToListAsync(ct);
        var used = new HashSet<Guid>();
        foreach (var line in d.Vaccinations)
        {
            var row = rows.FirstOrDefault(x => !used.Contains(x.Id) && (x.Id == line.Id
                || (string.Equals(x.VaccineName, line.VaccineName.Trim(), StringComparison.OrdinalIgnoreCase) && x.DoseNumber == line.DoseNumber)));
            if (row is null) continue;
            used.Add(row.Id);
            line.Id = row.Id;
            var x = line.Details();
            row.Manufacturer = x.Manufacturer; row.Route = x.Route; row.Site = x.Site; row.LotNumber = x.LotNumber; row.ExpiryDate = x.ExpiryDate;
            //JJQR - AI (21 Nov 2026) - restore what the replace-all dropped for an existing row (a new line keeps the defaults: today / Administered)
            if (keep is not null && keep.TryGetValue(row.Id, out var k))
            {
                row.NextDoseDate = k.NextDoseDate; row.AdministeredDate = k.AdministeredDate ?? row.AdministeredDate;
                row.ReactionNotes = k.ReactionNotes; row.Status = k.Status;
            }
            //JJQR - END
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }
}
//JJQR - END
