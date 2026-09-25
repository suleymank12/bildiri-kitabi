using BildiriKitabi.Core.Sanitization;

namespace BildiriKitabi.Core.Uploads;

public sealed record BookNameValidation(string Name, string? ErrorCode, string? ErrorMessage)
{
    public bool IsValid => ErrorCode is null;
}

/// <summary>
/// Checks the book name when the book is created. The name is printed on the cover, in every running head and in
/// the PDF metadata, so a name with contact details is rejected here instead of failing the post-render leak scan.
/// </summary>
public static class BookNameValidator
{
    public const int MinLength = 3;
    public const int MaxLength = 150;

    public static BookNameValidation Validate(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is < MinLength or > MaxLength)
        {
            return Invalid(trimmed, UploadErrorCodes.BookNameInvalid, $"Kitap adı {MinLength}–{MaxLength} karakter olmalıdır.");
        }

        if (trimmed.Any(char.IsControl))
        {
            return Invalid(trimmed, UploadErrorCodes.BookNameInvalid, "Kitap adı satır sonu veya kontrol karakteri içeremez.");
        }

        if (ContactInfoDetector.Find(trimmed).Count > 0)
        {
            return Invalid(
                trimmed,
                UploadErrorCodes.BookNameContainsContact,
                "Kitap adı e-posta adresi veya telefon numarası içeremez; bu bilgiler kitapta yer alamaz.");
        }

        return new BookNameValidation(trimmed, null, null);
    }

    private static BookNameValidation Invalid(string name, string code, string message) => new(name, code, message);
}
