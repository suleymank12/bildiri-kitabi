namespace BildiriKitabi.Core.Documents;

public interface IDocxReader
{
    /// <exception cref="InvalidDocumentException">The stream is not a readable Word document.</exception>
    SourceDocument Read(Stream stream);
}
