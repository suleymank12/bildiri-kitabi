namespace BildiriKitabi.Core.Documents;

public sealed class InvalidDocumentException : Exception
{
    public InvalidDocumentException()
    {
    }

    public InvalidDocumentException(string message)
        : base(message)
    {
    }

    public InvalidDocumentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
