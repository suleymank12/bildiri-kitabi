namespace BildiriKitabi.Core.Uploads;

/// <summary>Machine-readable codes of the validation errors returned when a book is created.</summary>
public static class UploadErrorCodes
{
    public const string BookNameInvalid = "BOOK_NAME_INVALID";
    public const string PaperCountInvalid = "PAPER_COUNT_INVALID";
    public const string FileExtensionInvalid = "FILE_EXTENSION_INVALID";
    public const string FileTooLarge = "FILE_TOO_LARGE";
    public const string FileEmpty = "FILE_EMPTY";
    public const string FileNotDocx = "FILE_NOT_DOCX";
    public const string FileUnsafeArchive = "FILE_UNSAFE_ARCHIVE";
    public const string FileNoContent = "FILE_NO_CONTENT";
    public const string FileDuplicate = "FILE_DUPLICATE";
    public const string TotalSizeTooLarge = "TOTAL_SIZE_TOO_LARGE";
}
