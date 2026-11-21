// JJQR - AI (Nov 21, 2026)
using ClinicApp.Domain.Enums;
using ClinicNode.Core;

namespace ClinicNode.Tests;

public class FeeCalculatorTests
{
    [Fact]
    public void New_visit_without_discount_or_cert_is_consultation_fee()
    {
        var preview = FeeCalculator.Compute(450, 350, 400, 50, VisitType.New, null, false);
        Assert.Equal(450m, preview.Total);
        Assert.Equal(preview.Total, preview.Parts.Sum(p => p.Subtract ? -p.Amount : p.Amount));
    }

    [Fact]
    public void Follow_up_plus_med_cert_plus_senior_discount()
    {
        var preview = FeeCalculator.Compute(450, 350, 400, 50, VisitType.FollowUp, "Senior", true);
        // subtotal = seniorPwd 400; + medCert 50 = 450
        Assert.Equal(450m, preview.Total);
        Assert.Equal(preview.Total, preview.Parts.Sum(p => p.Subtract ? -p.Amount : p.Amount));
        Assert.Contains(preview.Parts, p => p.Label == "Senior discount" && p.Subtract);
        Assert.Contains(preview.Parts, p => p.Label == "Medical certificate");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(0.01, true)]
    [InlineData(100_000, true)]
    [InlineData(100_000.01, false)]
    public void Charge_amount_bounds(decimal amount, bool expected) =>
        Assert.Equal(expected, FeeCalculator.IsValidChargeAmount(amount));
}
// JJQR - END
