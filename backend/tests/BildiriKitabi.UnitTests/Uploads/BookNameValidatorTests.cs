using BildiriKitabi.Core.Uploads;

namespace BildiriKitabi.UnitTests.Uploads;

public sealed class BookNameValidatorTests
{
    [Theory]
    [InlineData("Örnek Bilim Kongresi 2026")]
    [InlineData("  Bilişim Günleri  ")]
    [InlineData("ISBN 978-605-4220-65-3 Bildiriler")]
    [InlineData("2019-2023 Dönemi Çalışmaları")]
    public void Accepts_names_without_contact_details(string name)
    {
        var result = BookNameValidator.Validate(name);

        result.IsValid.ShouldBeTrue();
        result.Name.ShouldBe(name.Trim());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ab  ")]
    [InlineData("Satır\nsonu")]
    [InlineData("Sekme\tiçeren ad")]
    public void Rejects_too_short_names_and_control_characters(string? name)
    {
        BookNameValidator.Validate(name).ErrorCode.ShouldBe(UploadErrorCodes.BookNameInvalid);
    }

    [Fact]
    public void Rejects_names_longer_than_150_characters()
    {
        BookNameValidator.Validate(new string('a', 151)).ErrorCode.ShouldBe(UploadErrorCodes.BookNameInvalid);
        BookNameValidator.Validate(new string('a', 150)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Kongre 2026 - bilgi: 0312 555 12 34")]
    [InlineData("Kongre +90 312 555 12 34")]
    [InlineData("Kongre kongre@example.org")]
    [InlineData("Kongre (0500) 000 00 05")]
    public void Rejects_names_with_contact_details(string name)
    {
        var result = BookNameValidator.Validate(name);

        result.ErrorCode.ShouldBe(UploadErrorCodes.BookNameContainsContact);
        result.ErrorMessage.ShouldNotBeNullOrWhiteSpace();
    }
}
