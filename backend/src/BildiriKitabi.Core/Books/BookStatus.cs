namespace BildiriKitabi.Core.Books;

/// <summary>Lifecycle of a book; stored by name (the case requires <c>Durum = 'Failed'</c> verbatim).</summary>
public enum BookStatus
{
    Uploaded,
    Queued,
    Processing,
    Completed,
    Failed,
}
