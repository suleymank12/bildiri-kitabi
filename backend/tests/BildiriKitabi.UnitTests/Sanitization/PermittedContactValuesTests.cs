using BildiriKitabi.Core.Sanitization;

namespace BildiriKitabi.UnitTests.Sanitization;

public sealed class PermittedContactValuesTests
{
    private readonly PermittedContactValues _permitted =
        PermittedContactValues.FromText("Kongre 0312 555 12 34 Iletisim@Example.org");

    [Theory]
    [InlineData(ContactKind.Phone, "0312 555 12 34")]
    [InlineData(ContactKind.Phone, "0312-555-12-34")]
    [InlineData(ContactKind.Phone, "0312 555 12")]
    [InlineData(ContactKind.Email, "iletisim@example.org")]
    [InlineData(ContactKind.Email, "ILETISIM@EXAMPLE.ORG")]
    public void Values_from_the_book_name_and_their_pieces_are_permitted(ContactKind kind, string matched)
    {
        _permitted.Permits(kind, matched).ShouldBeTrue();
    }

    [Theory]
    [InlineData(ContactKind.Phone, "0500 000 99 99")]
    [InlineData(ContactKind.Phone, "0312 555 12 35")]
    [InlineData(ContactKind.Email, "baska@example.org")]
    [InlineData(ContactKind.Email, "0312 555 12 34")]
    public void Other_values_are_not_permitted(ContactKind kind, string matched)
    {
        _permitted.Permits(kind, matched).ShouldBeFalse();
    }

    [Fact]
    public void A_name_without_contact_details_permits_nothing()
    {
        PermittedContactValues.FromText("Örnek Bilim Kongresi 2026").Permits(ContactKind.Phone, "0312 555 12 34").ShouldBeFalse();
        PermittedContactValues.None.Permits(ContactKind.Email, "ad@example.org").ShouldBeFalse();
    }
}
