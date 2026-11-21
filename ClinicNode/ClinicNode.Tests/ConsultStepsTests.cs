// JJQR - AI (Nov 21, 2026)
using ClinicNode.Core;

namespace ClinicNode.Tests;

public class ConsultStepsTests
{
    [Fact]
    public void Diagnosis_step_is_hidden_from_nav_and_from_completion_gates()
    {
        Assert.False(ConsultSteps.ShowDiagnosisStep);
        Assert.DoesNotContain(ConsultSteps.DiagnosisSectionIndex, ConsultSteps.EffectiveRequiredSections);
        Assert.DoesNotContain(ConsultSteps.DiagnosisSectionIndex, ConsultSteps.VisibleSectionOrder);
        Assert.Equal(ConsultSteps.FeeSectionIndex, ConsultSteps.VisibleSectionOrder[^1]);
    }

    [Fact]
    public void Follow_up_prn_or_none_is_satisfied_without_a_date()
    {
        Assert.True(ConsultSteps.FollowUpSatisfied(FollowUpMode.Prn, null));
        Assert.True(ConsultSteps.FollowUpSatisfied(FollowUpMode.None, null));
        Assert.False(ConsultSteps.FollowUpSatisfied(FollowUpMode.Dated, null));
        Assert.True(ConsultSteps.FollowUpSatisfied(FollowUpMode.Dated, new DateOnly(2026, 11, 21)));
    }

    [Fact]
    public void Vitals_require_bp_and_hr_when_those_templates_exist()
    {
        var readings = new (string, string?)[] { ("blood_pressure", "120/80"), ("pulse_rate", ""), ("weight", "70") };
        Assert.False(ConsultSteps.VitalsSatisfied(readings));
        readings[1] = ("pulse_rate", "72");
        Assert.True(ConsultSteps.VitalsSatisfied(readings));
    }

    [Fact]
    public void Prescription_is_satisfied_by_no_meds_confirmation()
    {
        Assert.False(ConsultSteps.PrescriptionSatisfied(0, false));
        Assert.True(ConsultSteps.PrescriptionSatisfied(0, true));
        Assert.True(ConsultSteps.PrescriptionSatisfied(1, false));
    }

    [Fact]
    public void Fee_waive_requires_a_reason_and_charge_requires_a_sane_amount()
    {
        Assert.False(ConsultSteps.FeeSatisfied("Charge", 0, null));
        Assert.True(ConsultSteps.FeeSatisfied("Charge", 450, null));
        Assert.False(ConsultSteps.FeeSatisfied("Waive", null, "  "));
        Assert.True(ConsultSteps.FeeSatisfied("Waive", null, "Indigent"));
        Assert.False(ConsultSteps.FeeSatisfied(null, 450, "x"));
    }

    [Fact]
    public void Empty_required_step_is_an_empty_circle_until_complete_is_attempted()
    {
        Assert.Equal(StepIndicator.Empty, ConsultSteps.Indicator(false, required: true, hasDraft: false, attemptedComplete: false));
        Assert.Equal(StepIndicator.Warning, ConsultSteps.Indicator(false, required: true, hasDraft: false, attemptedComplete: true));
        Assert.Equal(StepIndicator.Complete, ConsultSteps.Indicator(true, required: true, hasDraft: false, attemptedComplete: true));
    }
}
// JJQR - END
