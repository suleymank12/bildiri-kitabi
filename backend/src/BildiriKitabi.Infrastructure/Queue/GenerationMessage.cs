using System.Text.Json;

namespace BildiriKitabi.Infrastructure.Queue;

/// <summary>The body of a generation message: <c>{"bookId":"…","version":1}</c>. The id is all a consumer needs.</summary>
public static class GenerationMessage
{
    public const int Version = 1;
    public const string ContentType = "application/json";

    public static byte[] Serialize(Guid bookId) =>
        JsonSerializer.SerializeToUtf8Bytes(new Body(bookId, Version), BodyJson.Options);

    /// <summary>False for anything that is not a JSON object with a non-empty <c>bookId</c> GUID.</summary>
    public static bool TryParse(ReadOnlySpan<byte> body, out Guid bookId)
    {
        bookId = Guid.Empty;
        try
        {
            var parsed = JsonSerializer.Deserialize<Body>(body, BodyJson.Options);
            if (parsed is { BookId: var id } && id != Guid.Empty)
            {
                bookId = id;
                return true;
            }
        }
        catch (JsonException)
        {
            // Not a message this application sent.
        }

        return false;
    }

    private sealed record Body(Guid BookId, int Version);

    private static class BodyJson
    {
        public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    }
}
