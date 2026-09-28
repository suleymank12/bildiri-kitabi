using System.Text.Json;

namespace BildiriKitabi.Infrastructure.Queue;

/// <summary>
/// The body of a generation message: <c>{"bookUid":"…","version":2}</c>, the book's external
/// <see cref="Core.Books.Book.Uid"/>; it is all a consumer needs.
/// </summary>
public static class GenerationMessage
{
    public const int Version = 2;
    public const string ContentType = "application/json";

    public static byte[] Serialize(Guid bookUid) =>
        JsonSerializer.SerializeToUtf8Bytes(new Body(bookUid, Version), BodyJson.Options);

    /// <summary>False for anything but a JSON object of this version with a non-empty <c>bookUid</c> GUID.</summary>
    public static bool TryParse(ReadOnlySpan<byte> body, out Guid bookUid)
    {
        bookUid = Guid.Empty;
        try
        {
            var parsed = JsonSerializer.Deserialize<Body>(body, BodyJson.Options);
            if (parsed is { Version: Version, BookUid: var uid } && uid != Guid.Empty)
            {
                bookUid = uid;
                return true;
            }
        }
        catch (JsonException)
        {
            // Not a message this application sent.
        }

        return false;
    }

    private sealed record Body(Guid BookUid, int Version);

    private static class BodyJson
    {
        public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    }
}
