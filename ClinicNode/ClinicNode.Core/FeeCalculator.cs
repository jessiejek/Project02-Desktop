// JJQR - AI (Nov 21, 2026) - port of Project02-fe/src/lib/clinical/preview-fee.ts computeClinicFee.
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;

namespace ClinicNode.Core;

public readonly record struct ClinicFeePart(string Label, decimal Amount, bool Subtract);

public sealed record ClinicFeePreview(decimal Total, IReadOnlyList<ClinicFeePart> Parts, string Summary);

/// <summary>
/// Same math as the web previewFee:
///   subtotal = discount ? seniorPwd : (FollowUp ? followUp : consultation)
///   total    = subtotal + (medCert ? medCert : 0)
/// </summary>
public static class FeeCalculator
{
    public static ClinicFeePreview Compute(
        ClinicSetting schedule,
        VisitType visitType,
        string? discountCategory,
        bool medCertRequested)
        => Compute(
            schedule.FeeConsultation,
            schedule.FeeFollowUp,
            schedule.FeeSeniorPwd,
            schedule.FeeMedCert,
            visitType,
            discountCategory,
            medCertRequested);

    public static ClinicFeePreview Compute(
        decimal consultation,
        decimal followUp,
        decimal seniorPwd,
        decimal medCert,
        VisitType visitType,
        string? discountCategory,
        bool medCertRequested)
    {
        var visitLabel = visitType == VisitType.FollowUp ? "Follow-up" : "Consultation";
        var visitAmount = visitType == VisitType.FollowUp ? followUp : consultation;
        var medAmount = medCertRequested ? medCert : 0m;
        var hasDiscount = !string.IsNullOrWhiteSpace(discountCategory);
        var subtotal = hasDiscount ? seniorPwd : visitAmount;
        var total = subtotal + medAmount;
        var parts = new List<ClinicFeePart> { new(visitLabel, visitAmount, Subtract: false) };
        if (medCertRequested) parts.Add(new("Medical certificate", medAmount, Subtract: false));
        if (hasDiscount)
        {
            var discLabel = string.Equals(discountCategory, "PWD", StringComparison.OrdinalIgnoreCase)
                ? "PWD discount" : "Senior discount";
            parts.Add(new(discLabel, visitAmount - seniorPwd, Subtract: true));
        }

        var breakdown = string.Join("", parts.Select((part, i) =>
        {
            var piece = $"{part.Label} {FormatPeso(part.Amount)}";
            if (i == 0) return piece;
            return part.Subtract ? $" − {piece}" : $" + {piece}";
        }));
        return new ClinicFeePreview(total, parts, $"Total: {FormatPeso(total)} ({breakdown})");
    }

    /// <summary>A Charge amount must be &gt; 0 and at most ₱100,000 (server rule on consult upsert).</summary>
    public static bool IsValidChargeAmount(decimal amount) => amount > 0m && amount <= 100_000m;

    public static string FormatPeso(decimal amount) =>
        "₱" + amount.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("en-PH"));
}
// JJQR - END
