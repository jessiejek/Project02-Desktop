//JJQR - AI (Nov 21, 2026) - port of key cases from Project02-fe rx-sig-parse.test.mjs.
using ClinicNode.Core.Printing;

namespace ClinicNode.Tests.Printing;

public class RxSigTests
{
    const string B = RxSig.Breakfast, L = RxSig.Lunch, D = RxSig.Dinner, N = RxSig.Bedtime;

    [Fact]
    public void Structured_meal_wins_over_text()
    {
        var r = RxSig.Parse("after meals", mealRelation: "Before");
        Assert.Equal("before", r.Meal);
        Assert.Equal([B, L, D], r.Slots);
    }

    [Theory]
    [InlineData("once daily", new[] { B })]
    [InlineData("once daily at night", new[] { N })]
    [InlineData("BID", new[] { B, D })]
    [InlineData("1 tab q8h", new[] { B, L, D })]
    [InlineData("every 6 hours", new[] { B, L, D, N })]
    [InlineData("BID with meals", new[] { B, D })]
    [InlineData("morning and evening", new[] { B, D })]
    public void Frequency_and_time_words_map_to_slots(string sig, string[] slots) =>
        Assert.Equal(slots, RxSig.Parse(sig).Slots);

    [Theory]
    [InlineData("prn for fever")]
    [InlineData("after each loose stool")]
    [InlineData("for pain")]
    public void As_needed_sigs_have_no_slots(string sig)
    {
        var r = RxSig.Parse(sig);
        Assert.True(r.AsNeeded);
        Assert.Empty(r.Slots);
    }

    [Fact]
    public void Free_text_without_timing_is_as_directed()
    {
        var r = RxSig.Parse("take 2 puffs");
        Assert.True(r.AsDirected);
        Assert.False(RxSig.Parse("").AsDirected);
    }

    [Theory]
    [InlineData("Paracetamol 500mg tablet", "500 mg", "Paracetamol 500mg tablet")]
    [InlineData("Paracetamol", "500 mg", "Paracetamol 500 mg")]
    [InlineData("ORS SACHET", "1 sachet", "ORS SACHET")]
    [InlineData("Losartan", "", "Losartan")]
    public void DisplayName_drops_duplicated_strength(string generic, string dose, string expected) =>
        Assert.Equal(expected, RxSig.DisplayName(generic, dose));
}
//JJQR - END
