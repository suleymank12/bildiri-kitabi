using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BildiriKitabi.Api.Contracts;

namespace BildiriKitabi.IntegrationTests.Api;

/// <summary>The numeric database ids never leave the API: every id in a response is the Uid.</summary>
public sealed class IdentityExposureTests(SqlServerFixture sql) : IAsyncLifetime
{
    private ApiHost _api = null!;

    public ValueTask InitializeAsync()
    {
        if (sql.IsAvailable)
        {
            _api = new ApiHost(sql.NewDatabase(), ApiHost.NewDataRoot());
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
            SchemaAndUploadTests.TryDelete(_api.DataRoot);
        }
    }

    [Fact]
    public async Task No_response_contains_a_numeric_id()
    {
        sql.EnsureAvailable();
        using var client = _api.Client();
        var responses = new List<JsonElement>();

        using var upload = ApiHost.SampleUpload("Kimlik Denemesi");
        using var created = await client.PostAsync(new Uri("/api/books", UriKind.Relative), upload, TestContext.Current.CancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var createdBody = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        responses.Add(createdBody);
        var uid = createdBody.GetProperty("id").GetGuid();
        var paperUids = createdBody.GetProperty("papers").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();

        // The ids in the response are the Uid columns, and the storage keys are built from them.
        var row = await _api.QueryAsync("SELECT Id FROM Kitaplar WHERE Uid = @id", r => r.GetInt32(0), ("@id", uid));
        var numericId = row.ShouldHaveSingleItem();
        (await _api.QueryAsync("SELECT Uid FROM Bildiriler WHERE KitapId = @id ORDER BY SiraNo", r => r.GetGuid(0), ("@id", numericId))).ShouldBe(paperUids);
        created.Headers.Location!.AbsolutePath.ShouldBe($"/api/books/{uid}");
        _api.StoredFiles(uid).Count.ShouldBe(10);

        using (var reordered = await client.PutAsJsonAsync(
            new Uri($"/api/books/{uid}/paper-order", UriKind.Relative),
            new PaperOrderRequest(paperUids.AsEnumerable().Reverse().ToList()),
            ApiHost.Json,
            TestContext.Current.CancellationToken))
        {
            reordered.StatusCode.ShouldBe(HttpStatusCode.OK);
            responses.Add(await reordered.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken));
        }

        (await _api.GenerateAsync(uid)).ShouldBe(HttpStatusCode.Accepted);
        await _api.WaitForFinalStatusAsync(uid);
        responses.Add(await client.GetFromJsonAsync<JsonElement>(new Uri($"/api/books/{uid}", UriKind.Relative), TestContext.Current.CancellationToken));
        responses.Add(await client.GetFromJsonAsync<JsonElement>(new Uri("/api/books", UriKind.Relative), TestContext.Current.CancellationToken));
        using (var deleted = await client.DeleteAsync(new Uri($"/api/books/{uid}", UriKind.Relative), TestContext.Current.CancellationToken))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        responses.Add(await client.GetFromJsonAsync<JsonElement>(new Uri("/api/books/deleted", UriKind.Relative), TestContext.Current.CancellationToken));

        // A numeric id in the address is not a book address at all.
        using var byNumber = await client.GetAsync(new Uri($"/api/books/{numericId}", UriKind.Relative), TestContext.Current.CancellationToken);
        byNumber.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        foreach (var response in responses)
        {
            IdProperties(response).ShouldNotBeEmpty();
            foreach (var (path, value) in IdProperties(response))
            {
                value.ValueKind.ShouldBe(JsonValueKind.String, path);
                Guid.TryParse(value.GetString(), out _).ShouldBeTrue(path);
            }
        }
    }

    /// <summary>Every property named <c>id</c> or ending in <c>Id</c>, anywhere in the document.</summary>
    internal static IEnumerable<(string Path, JsonElement Value)> IdProperties(JsonElement element, string path = "$")
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var propertyPath = $"{path}.{property.Name}";
                if (property.Name.EndsWith("id", StringComparison.OrdinalIgnoreCase) && !property.Name.EndsWith("traceId", StringComparison.Ordinal))
                {
                    yield return (propertyPath, property.Value);
                }

                foreach (var nested in IdProperties(property.Value, propertyPath))
                {
                    yield return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in IdProperties(item, $"{path}[{i++}]"))
                {
                    yield return nested;
                }
            }
        }
    }
}
