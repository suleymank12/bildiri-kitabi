using BildiriKitabi.Core.Books;

namespace BildiriKitabi.UnitTests.Books;

public sealed class PdfFileNameTests
{
    [Theory]
    [InlineData("Örnek Bilim Kongresi 2026", "bildiri-kitabi-ornek-bilim-kongresi-2026.pdf")]
    [InlineData("İŞLETME ÇALIŞTAYI – Güz Dönemi", "bildiri-kitabi-isletme-calistayi-guz-donemi.pdf")]
    [InlineData("ığüşöç ĞÜŞİÖÇ", "bildiri-kitabi-igusoc-gusioc.pdf")]
    [InlineData("  a/b\\c:d*e?f\"g<h>i|j  ", "bildiri-kitabi-a-b-c-d-e-f-g-h-i-j.pdf")]
    [InlineData("Café Résumé", "bildiri-kitabi-cafe-resume.pdf")]
    [InlineData("日本語", "bildiri-kitabi.pdf")]
    public void Ascii_fallback_is_a_safe_lower_case_slug(string bookName, string expected)
    {
        PdfFileName.Ascii(bookName).ShouldBe(expected);
    }

    [Fact]
    public void Unicode_name_keeps_turkish_letters_and_drops_characters_not_allowed_in_file_names()
    {
        PdfFileName.Unicode("Örnek Bilim Kongresi 2026").ShouldBe("Örnek Bilim Kongresi 2026.pdf");
        PdfFileName.Unicode("Rapor: 2026/1 \"Taslak\"").ShouldBe("Rapor 2026 1 Taslak.pdf");
    }

    [Fact]
    public void Content_disposition_carries_both_names()
    {
        PdfFileName.ContentDisposition("Örnek Bilim Kongresi 2026", download: true).ShouldBe(
            "attachment; filename=\"bildiri-kitabi-ornek-bilim-kongresi-2026.pdf\"; " +
            "filename*=UTF-8''%C3%96rnek%20Bilim%20Kongresi%202026.pdf");
        PdfFileName.ContentDisposition("Kongre", download: false).ShouldStartWith("inline; ");
    }

    [Fact]
    public void Content_disposition_is_plain_ascii()
    {
        var header = PdfFileName.ContentDisposition("Kongre (2026); \"ÖZEL\" sayı ığüşöç", download: true);

        header.All(c => c is >= ' ' and <= '~').ShouldBeTrue();
        header.ShouldContain("filename*=UTF-8''Kongre%20%282026%29%3B%20%C3%96ZEL%20say%C4%B1%20%C4%B1%C4%9F%C3%BC%C5%9F%C3%B6%C3%A7.pdf");
    }
}
