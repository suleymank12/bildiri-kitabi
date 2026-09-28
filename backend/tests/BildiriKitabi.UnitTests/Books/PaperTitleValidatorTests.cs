using BildiriKitabi.Core.Books;
using BildiriKitabi.Infrastructure.Pdf;

namespace BildiriKitabi.UnitTests.Books;

public sealed class PaperTitleValidatorTests
{
    [Theory]
    [InlineData("  KENTSEL TARIMDA AKILLI SULAMA  ", "KENTSEL TARIMDA AKILLI SULAMA")]
    [InlineData("Kentsel Tarımda\nAkıllı Sulama", "Kentsel Tarımda Akıllı Sulama")]
    [InlineData("Kentsel Tarımda\r\nAkıllı Sulama", "Kentsel Tarımda Akıllı Sulama")]
    [InlineData("2019-2023 Yıllarında Şehir İçi Ulaşım: ORCID 0000-0002-1825-009X", "2019-2023 Yıllarında Şehir İçi Ulaşım: ORCID 0000-0002-1825-009X")]
    [InlineData("Öğrenci’lerin %24'ü; p<0.05", "Öğrenci’lerin %24'ü; p<0.05")]
    public void A_title_is_trimmed_and_line_breaks_become_spaces(string input, string expected)
    {
        var result = Validate(input);

        result.IsValid.ShouldBeTrue(result.ErrorMessage);
        result.Title.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, "Bildiri başlığı boş olamaz.")]
    [InlineData("", "Bildiri başlığı boş olamaz.")]
    [InlineData(" \r\n ", "Bildiri başlığı boş olamaz.")]
    [InlineData("Sekme\tiçeren başlık", "Bildiri başlığı kontrol karakteri içeremez.")]
    public void Empty_titles_and_control_characters_are_invalid(string? input, string message)
    {
        var result = Validate(input);

        result.ErrorCode.ShouldBe(PaperTitleValidator.InvalidCode);
        result.ErrorMessage.ShouldBe(message);
    }

    [Fact]
    public void A_title_may_have_500_characters_but_not_more()
    {
        Validate(new string('a', Paper.TitleMaxLength)).IsValid.ShouldBeTrue();

        var result = Validate(new string('a', Paper.TitleMaxLength + 1));

        result.ErrorCode.ShouldBe(PaperTitleValidator.InvalidCode);
        result.ErrorMessage.ShouldBe("Bildiri başlığı en fazla 500 karakter olabilir.");
    }

    [Theory]
    [InlineData("Akıllı Sulama elif.kaya@example.org")]
    [InlineData("Akıllı Sulama ad [at] ornek [dot] edu [dot] tr")]
    [InlineData("Akıllı Sulama 0500 000 00 01")]
    [InlineData("Akıllı Sulama +90 312 555 12 34")]
    public void Contact_details_are_not_allowed(string input)
    {
        var result = Validate(input);

        result.ErrorCode.ShouldBe(PaperTitleValidator.ContactInfoCode);
        result.ErrorMessage.ShouldBe("Başlıkta e-posta adresi veya telefon numarası bulunamaz.");
    }

    [Fact]
    public void Characters_the_pdf_fonts_cannot_print_are_not_allowed()
    {
        var result = Validate("Akıllı Sulama 😀");

        result.ErrorCode.ShouldBe(PaperTitleValidator.UnsupportedCharacterCode);
        result.ErrorMessage.ShouldBe("Başlıktaki '😀' karakteri PDF yazı tipinde bulunmuyor; lütfen kaldırın.");
    }

    private static PaperTitleValidation Validate(string? title) => PaperTitleValidator.Validate(title, FontGlyphCoverage.Instance);
}
