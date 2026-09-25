namespace BildiriKitabi.Api.Problems;

/// <summary>
/// The shape of every error response (RFC 9457 ProblemDetails plus <c>code</c>, <c>errors</c> and <c>traceId</c>).
/// Only used to describe responses in the OpenAPI document, so clients get a typed error model.
/// </summary>
public sealed class ProblemResponse
{
    public string? Type { get; init; }

    public required string Title { get; init; }

    public required int Status { get; init; }

    /// <summary>Turkish, user-facing explanation.</summary>
    public required string Detail { get; init; }

    /// <summary>Machine-readable error code, for example <c>PAPER_COUNT_INVALID</c>.</summary>
    public required string Code { get; init; }

    public string? TraceId { get; init; }

    /// <summary>Individual problems, each about a form field or an uploaded file.</summary>
    public IReadOnlyList<ApiError>? Errors { get; init; }
}
