using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Titles;
using BildiriKitabi.Core.Uploads;
using BildiriKitabi.Tests.Shared;
using Microsoft.Data.SqlClient;

namespace BildiriKitabi.IntegrationTests.Api;

public sealed class SchemaAndUploadTests(SqlServerFixture sql) : IAsyncLifetime
{
    private ApiHost _api = null!;

    public ValueTask InitializeAsync()
    {
        if (!sql.IsAvailable)
        {
            return ValueTask.CompletedTask;
        }

        _api = new ApiHost(sql.NewDatabase(), ApiHost.NewDataRoot());
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
            TryDelete(_api.DataRoot);
        }
    }

    [Fact]
    public async Task Migration_creates_the_tables_keys_and_constraints_on_an_empty_database()
    {
        sql.EnsureAvailable();
        using var client = _api.Client();
        (await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var tables = await _api.QueryAsync("SELECT name FROM sys.tables WHERE name <> '__EFMigrationsHistory'", r => r.GetString(0));
        tables.ShouldBe(["Bildiriler", "Kitaplar"], ignoreOrder: true);
        (await _api.QueryAsync("SELECT MigrationId FROM __EFMigrationsHistory", r => r.GetString(0))).ShouldHaveSingleItem().ShouldEndWith("_InitialCreate");

        // Both tables: int IDENTITY(1,1) primary key on Id, and a Uid column.
        var primaryKeys = await _api.QueryAsync(
            """
            SELECT OBJECT_NAME(i.object_id), COL_NAME(ic.object_id, ic.column_id), c.is_identity, i.type_desc
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.is_primary_key = 1 AND OBJECT_NAME(i.object_id) IN ('Kitaplar', 'Bildiriler')
            """,
            r => $"{r.GetString(0)}.{r.GetString(1)} identity={r.GetBoolean(2)} {r.GetString(3)}");
        primaryKeys.ShouldBe(["Kitaplar.Id identity=True CLUSTERED", "Bildiriler.Id identity=True CLUSTERED"], ignoreOrder: true);
        (await _api.QueryAsync(
            "SELECT OBJECT_NAME(object_id), CAST(seed_value AS int), CAST(increment_value AS int) FROM sys.identity_columns WHERE OBJECT_NAME(object_id) IN ('Kitaplar', 'Bildiriler')",
            r => $"{r.GetString(0)}({r.GetInt32(1)},{r.GetInt32(2)})")).ShouldBe(["Kitaplar(1,1)", "Bildiriler(1,1)"], ignoreOrder: true);

        var foreignKeys = await _api.QueryAsync(
            """
            SELECT OBJECT_NAME(fk.parent_object_id), OBJECT_NAME(fk.referenced_object_id), COL_NAME(c.parent_object_id, c.parent_column_id), fk.delete_referential_action_desc
            FROM sys.foreign_keys fk JOIN sys.foreign_key_columns c ON c.constraint_object_id = fk.object_id
            """,
            r => $"{r.GetString(0)}.{r.GetString(2)} -> {r.GetString(1)} {r.GetString(3)}");
        foreignKeys.ShouldBe(["Bildiriler.KitapId -> Kitaplar CASCADE"]);

        var uniqueIndexes = await _api.QueryAsync(
            """
            SELECT i.name, STRING_AGG(COL_NAME(ic.object_id, ic.column_id), ',') WITHIN GROUP (ORDER BY ic.key_ordinal)
            FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            WHERE i.object_id = OBJECT_ID('Bildiriler') AND i.is_unique = 1 AND i.is_primary_key = 0
            GROUP BY i.name
            """,
            r => $"{r.GetString(0)}({r.GetString(1)})");
        uniqueIndexes.ShouldBe(["UX_Bildiriler_KitapId_Sha256(KitapId,Sha256)", "UX_Bildiriler_KitapId_SiraNo(KitapId,SiraNo)", "UX_Bildiriler_Uid(Uid)"], ignoreOrder: true);


        var bookIndexes = await _api.QueryAsync(
            """
            SELECT i.name, COL_NAME(ic.object_id, ic.column_id), ic.is_descending_key
            FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            WHERE i.object_id = OBJECT_ID('Kitaplar') AND i.is_primary_key = 0
            """,
            r => $"{r.GetString(0)}:{r.GetString(1)}:{(r.GetBoolean(2) ? "DESC" : "ASC")}");
        bookIndexes.ShouldBe(["IX_Kitaplar_Durum:Durum:ASC", "IX_Kitaplar_OlusturulmaZamani:OlusturulmaZamani:DESC", "UX_Kitaplar_Uid:Uid:ASC"], ignoreOrder: true);
        (await _api.QueryAsync("SELECT is_unique FROM sys.indexes WHERE name = 'UX_Kitaplar_Uid'", r => r.GetBoolean(0))).ShouldBe([true]);

        var checks = await _api.QueryAsync("SELECT name, definition FROM sys.check_constraints", r => (Name: r.GetString(0), Definition: r.GetString(1)));
        checks.Select(c => c.Name).ShouldBe(
            ["CK_Bildiriler_SiraNo", "CK_Bildiriler_YuklemeSirasi", "CK_Kitaplar_Basarisiz_Mesaj", "CK_Kitaplar_Durum", "CK_Kitaplar_IlerlemeYuzdesi", "CK_Kitaplar_Silinme", "CK_Kitaplar_Tamamlandi_Pdf"],
            ignoreOrder: true);
        checks.Single(c => c.Name == "CK_Kitaplar_Durum").Definition.ShouldContain("'Failed'");

        var columns = await _api.QueryAsync(
            "SELECT TABLE_NAME + '.' + COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE, COLUMN_DEFAULT FROM INFORMATION_SCHEMA.COLUMNS",
            r => (Name: r.GetString(0), Type: r.GetString(1), Length: r.IsDBNull(2) ? (int?)null : r.GetInt32(2), Nullable: r.GetString(3) == "YES", Default: r.IsDBNull(4) ? null : r.GetString(4)));
        string Column(string name)
        {
            var c = columns.Single(c => c.Name == name);
            return $"{c.Type}{(c.Length is { } length ? $"({length})" : string.Empty)} {(c.Nullable ? "NULL" : "NOT NULL")}";
        }

        Column("Kitaplar.Id").ShouldBe("int NOT NULL");
        Column("Kitaplar.Uid").ShouldBe("uniqueidentifier NOT NULL");
        Column("Kitaplar.Ad").ShouldBe("nvarchar(150) NOT NULL");
        Column("Kitaplar.Durum").ShouldBe("nvarchar(20) NOT NULL");
        Column("Kitaplar.IlerlemeYuzdesi").ShouldBe("tinyint NOT NULL");
        Column("Kitaplar.OlusturulmaZamani").ShouldBe("datetime2 NOT NULL");
        Column("Kitaplar.AktifMi").ShouldBe("bit NOT NULL");
        Column("Kitaplar.SilinmeZamani").ShouldBe("datetime2 NULL");
        Column("Kitaplar.PdfDepolamaAnahtari").ShouldBe("nvarchar(260) NULL");
        Column("Kitaplar.SatirVersiyonu").ShouldBe("timestamp NOT NULL");
        Column("Bildiriler.Id").ShouldBe("int NOT NULL");
        Column("Bildiriler.Uid").ShouldBe("uniqueidentifier NOT NULL");
        Column("Bildiriler.KitapId").ShouldBe("int NOT NULL");
        Column("Bildiriler.Sha256").ShouldBe("binary(32) NOT NULL");
        Column("Bildiriler.OrijinalDosyaAdi").ShouldBe("nvarchar(255) NOT NULL");
        Column("Bildiriler.BaslangicSayfasi").ShouldBe("int NULL");
        columns.Single(c => c.Name == "Kitaplar.AktifMi").Default.ShouldBe("(CONVERT([bit],(1)))");
        columns.ShouldNotContain(c => c.Name == "Bildiriler.AktifMi");
    }

    [Fact]
    public async Task A_row_written_by_hand_is_active_by_default_and_uids_are_unique()
    {
        sql.EnsureAvailable();
        using (_api.Client())
        {
            var uid = Guid.NewGuid();
            const string Insert = "INSERT INTO Kitaplar (Uid, Ad, Durum) VALUES (@uid, N'Elle Eklenen', 'Uploaded')";
            await _api.ExecuteAsync(Insert, ("@uid", uid));

            (await _api.QueryAsync("SELECT AktifMi, SilinmeZamani FROM Kitaplar WHERE Uid = @uid", r => (r.GetBoolean(0), r.IsDBNull(1)), ("@uid", uid)))
                .ShouldBe([(true, true)]);
            var duplicate = await Should.ThrowAsync<SqlException>(() => _api.ExecuteAsync(Insert, ("@uid", uid)));
            duplicate.Message.ShouldContain("UX_Kitaplar_Uid");
        }
    }

    [Fact]
    public async Task Check_constraints_reject_a_completed_book_without_pdf_and_a_failed_book_without_message()
    {
        sql.EnsureAvailable();
        var book = await _api.CreateSampleBookAsync();

        var completed = await Should.ThrowAsync<SqlException>(() => _api.ExecuteAsync("UPDATE Kitaplar SET Durum = 'Completed' WHERE Uid = @id", ("@id", book.Uid)));
        completed.Message.ShouldContain("CK_Kitaplar_Tamamlandi_Pdf");
        var failed = await Should.ThrowAsync<SqlException>(() => _api.ExecuteAsync("UPDATE Kitaplar SET Durum = 'Failed' WHERE Uid = @id", ("@id", book.Uid)));
        failed.Message.ShouldContain("CK_Kitaplar_Basarisiz_Mesaj");
        var unknown = await Should.ThrowAsync<SqlException>(() => _api.ExecuteAsync("UPDATE Kitaplar SET Durum = 'Done' WHERE Uid = @id", ("@id", book.Uid)));
        unknown.Message.ShouldContain("CK_Kitaplar_Durum");
        var deletedWithoutTime = await Should.ThrowAsync<SqlException>(() => _api.ExecuteAsync("UPDATE Kitaplar SET AktifMi = 0 WHERE Uid = @id", ("@id", book.Uid)));
        deletedWithoutTime.Message.ShouldContain("CK_Kitaplar_Silinme");
        var activeWithTime = await Should.ThrowAsync<SqlException>(() => _api.ExecuteAsync("UPDATE Kitaplar SET SilinmeZamani = SYSUTCDATETIME() WHERE Uid = @id", ("@id", book.Uid)));
        activeWithTime.Message.ShouldContain("CK_Kitaplar_Silinme");
    }

    [Fact]
    public async Task Ten_sample_papers_create_a_book_with_ordered_papers_and_detected_titles()
    {
        sql.EnsureAvailable();
        using var client = _api.Client();
        using var upload = ApiHost.SampleUpload("Örnek Bilim Kongresi 2026");

        using var response = await client.PostAsync(new Uri("/api/books", UriKind.Relative), upload, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var book = (await response.Content.ReadFromJsonAsync<BildiriKitabi.Api.Contracts.BookDetailDto>(ApiHost.Json, TestContext.Current.CancellationToken))!;
        response.Headers.Location!.AbsolutePath.ShouldBe($"/api/books/{book.Uid}");
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("default-src 'none'");

        book.Name.ShouldBe("Örnek Bilim Kongresi 2026");
        book.Status.ShouldBe(BookStatus.Uploaded);
        book.PdfUrl.ShouldBeNull();
        book.Papers.Select(p => p.Order).ShouldBe(Enumerable.Range(1, 10));
        book.Papers.Select(p => p.UploadOrder).ShouldBe(Enumerable.Range(1, 10));
        book.Papers.Select(p => p.FileName).ShouldBe(TestPaths.PaperFiles.Select(Path.GetFileName).ToList()!);
        book.Papers.ShouldAllBe(p => p.TitleSource == TitleSource.TitleStyle && p.StartPage == null && p.SizeBytes > 0);
        book.Papers[0].Title.ShouldBe("KENTSEL TARIMDA AKILLI SULAMA SİSTEMLERİNİN SU TÜKETİMİNE ETKİSİ");
        book.Papers[9].Title.ShouldBe("MÜZE ZİYARETLERİNDE KİŞİSELLEŞTİRİLMİŞ DİJİTAL REHBERLERİN DENEYİME ETKİSİ");

        var rows = await _api.QueryAsync(
            "SELECT SiraNo, OrijinalDosyaAdi, DepolamaAnahtari, DATALENGTH(Sha256) FROM Bildiriler WHERE KitapId = (SELECT Id FROM Kitaplar WHERE Uid = @id) ORDER BY SiraNo",
            r => (Order: r.GetInt32(0), Name: r.GetString(1), Key: r.GetString(2), HashLength: r.GetInt32(3)),
            ("@id", book.Uid));
        rows.Count.ShouldBe(10);
        rows.ShouldAllBe(r => r.HashLength == 32 && r.Key.StartsWith($"books/{book.Uid}/sources/", StringComparison.Ordinal) && r.Key.EndsWith(".docx", StringComparison.Ordinal));
        _api.StoredFiles(book.Uid).Count.ShouldBe(10);
    }

    [Fact]
    public async Task Invalid_uploads_are_rejected_with_their_codes_and_leave_no_trace()
    {
        sql.EnsureAvailable();
        using var client = _api.Client();
        var paths = TestPaths.PaperFiles;

        // Nine files and a name that is too short: both reported in one response.
        using (var nine = ApiHost.SampleUpload("ab", paths.Take(9)))
        {
            var problem = await PostExpectingProblemAsync(client, nine);
            problem.GetProperty("code").GetString().ShouldBe("VALIDATION_FAILED");
            ErrorCodes(problem).ShouldBe([UploadErrorCodes.BookNameInvalid, UploadErrorCodes.PaperCountInvalid], ignoreOrder: true);
        }

        // A renamed PDF among ten files.
        using (var renamed = ApiHost.SampleUpload("Örnek Bilim Kongresi 2026", paths.Take(9)))
        {
            ApiHost.AddFile(renamed, "10_Muze_Deneyimi.docx", Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF"));
            var problem = await PostExpectingProblemAsync(client, renamed);
            problem.GetProperty("code").GetString().ShouldBe(UploadErrorCodes.FileNotDocx);
            var error = problem.GetProperty("errors").EnumerateArray().Single();
            error.GetProperty("fileName").GetString().ShouldBe("10_Muze_Deneyimi.docx");
            error.GetProperty("message").GetString().ShouldBe("10_Muze_Deneyimi.docx geçerli bir Word (.docx) belgesi değil.");
            problem.GetProperty("detail").GetString().ShouldBe("10_Muze_Deneyimi.docx geçerli bir Word (.docx) belgesi değil.");
            problem.TryGetProperty("traceId", out _).ShouldBeTrue();
        }

        // The same paper twice and a .doc file.
        using (var duplicate = ApiHost.SampleUpload("Örnek Bilim Kongresi 2026", paths.Take(8)))
        {
            ApiHost.AddFile(duplicate, "kopya.docx", await File.ReadAllBytesAsync(paths[0], TestContext.Current.CancellationToken));
            ApiHost.AddFile(duplicate, "eski.doc", [1, 2, 3]);
            var problem = await PostExpectingProblemAsync(client, duplicate);
            ErrorCodes(problem).ShouldBe([UploadErrorCodes.FileDuplicate, UploadErrorCodes.FileExtensionInvalid], ignoreOrder: true);
        }

        // A name with a character the PDF fonts cannot print.
        using (var emoji = ApiHost.SampleUpload("Kongre 2026 😀"))
        {
            var problem = await PostExpectingProblemAsync(client, emoji);
            problem.GetProperty("code").GetString().ShouldBe(UploadErrorCodes.BookNameUnsupportedCharacter);
            var error = problem.GetProperty("errors").EnumerateArray().Single();
            error.GetProperty("field").GetString().ShouldBe("name");
            error.GetProperty("message").GetString().ShouldBe("Kitap adındaki '😀' karakteri PDF yazı tipinde bulunmuyor; lütfen kaldırın.");
        }

        (await _api.QueryAsync("SELECT COUNT(*) FROM Kitaplar", r => r.GetInt32(0))).ShouldBe([0]);
        (await _api.QueryAsync("SELECT COUNT(*) FROM Bildiriler", r => r.GetInt32(0))).ShouldBe([0]);
        _api.StoredFiles().ShouldBeEmpty();
        (Directory.Exists(_api.UploadTempRoot) ? Directory.GetFileSystemEntries(_api.UploadTempRoot) : []).ShouldBeEmpty();
    }

    private static async Task<JsonElement> PostExpectingProblemAsync(HttpClient client, MultipartFormDataContent content)
    {
        using var response = await client.PostAsync(new Uri("/api/books", UriKind.Relative), content, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json", body);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private static List<string> ErrorCodes(JsonElement problem) =>
        problem.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("code").GetString()!).ToList();

    internal static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (IOException)
        {
            // A file may still be closing; the folder is under the temp path.
        }
    }
}
