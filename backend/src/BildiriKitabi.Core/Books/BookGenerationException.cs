namespace BildiriKitabi.Core.Books;

public static class BookErrorCodes
{
    public const string InvalidDocument = "INVALID_DOCUMENT";
    public const string ContactLeakDetected = "CONTACT_LEAK_DETECTED";
    public const string RenderFailed = "RENDER_FAILED";
    public const string UnsupportedCharacter = "UNSUPPORTED_CHARACTER";
    public const string GenerationTimeout = "GENERATION_TIMEOUT";
    public const string InternalError = "INTERNAL_ERROR";
}

/// <summary>A generation failure with a machine-readable code and a message that can be shown to the user.</summary>
public sealed class BookGenerationException : Exception
{
    public BookGenerationException()
        : this(BookErrorCodes.RenderFailed, "Kitap oluşturulamadı.")
    {
    }

    public BookGenerationException(string message)
        : this(BookErrorCodes.RenderFailed, message)
    {
    }

    public BookGenerationException(string message, Exception innerException)
        : this(BookErrorCodes.RenderFailed, message, innerException)
    {
    }

    public BookGenerationException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
