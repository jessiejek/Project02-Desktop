//JJQR - AI (2026-11-27) - why: Enter auto-numbering in the "Each on its own line" Diagnosis box (same cases as Project02-fe dx-auto-number.test.mjs)
using ClinicNode.Core.Doctor;

namespace ClinicNode.Tests.DoctorExtra;

public class DxAutoNumberTests
{
    static DxEdit? EnterAt(string s, int i, string nl = "\n") => DxAutoNumber.Edit(s, s[..i] + nl + s[i..]);
    static DxEdit? Type(string s, int i, char ch) => DxAutoNumber.Edit(s, s[..i] + ch + s[i..]);

    [Fact]
    public void Enter_after_a_numbered_line_adds_the_next_number()
    {
        Assert.Equal(new DxEdit("1) URTI\n2) ", 11), EnterAt("1) URTI", 7));
        Assert.Equal(new DxEdit("1) URTI\n2) DENGUE FEVER\n3) ", 27), EnterAt("1) URTI\n2) DENGUE FEVER", 23));
        Assert.Equal(new DxEdit("9. AGE\n10. ", 11), EnterAt("9. AGE", 6));
        Assert.Equal(new DxEdit("1) URTI\r2) ", 11), EnterAt("1) URTI", 7, "\r"));
    }

    [Fact]
    public void Enter_on_a_blank_numbered_line_removes_the_marker()
    {
        Assert.Equal(new DxEdit("1) URTI\n", 8), EnterAt("1) URTI\n2) ", 11));
        Assert.Equal(new DxEdit("1) URTI\n", 8), EnterAt("1) URTI\n2)", 10));
    }

    [Fact]
    public void A_number_typed_after_the_automatic_one_is_not_doubled()
    {
        Assert.Equal(new DxEdit("1) URTI\n2)", 10), Type("1) URTI\n2) 2", 12, ')'));
        Assert.Equal(new DxEdit("1) URTI\n3.", 10), Type("1) URTI\n2) 3", 12, '.'));
    }

    [Fact]
    public void No_change_for_plain_text_existing_markers_paste_crlf_and_ordinary_typing()
    {
        Assert.Null(EnterAt("URTI", 4));
        Assert.Null(EnterAt("1) URTI 2) AGE", 8));
        Assert.Null(DxAutoNumber.Edit("1) URTI", "1) URTI\n2) AGE"));
        Assert.Null(DxAutoNumber.Edit("1) URTI\r", "1) URTI\r\n"));
        Assert.Null(Type("1) URT", 6, 'I'));
        Assert.Null(Type("Paracetamol 2", 13, '.'));
    }

    [Fact]
    public void Enter_in_the_middle_moves_the_rest_to_the_new_numbered_line() =>
        Assert.Equal(new DxEdit("1) URTI\n2) DENGUE", 11), EnterAt("1) URTI DENGUE", 7));
}
//JJQR - END
