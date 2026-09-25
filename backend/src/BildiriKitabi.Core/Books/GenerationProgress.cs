namespace BildiriKitabi.Core.Books;

public enum GenerationStage
{
    Reading,
    Sanitizing,
    Composing,
    Rendering,
    Verifying,
    Saving,
}

public sealed record GenerationProgress(GenerationStage Stage, int Percent);
