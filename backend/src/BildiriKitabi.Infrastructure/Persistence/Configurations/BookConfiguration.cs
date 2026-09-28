using BildiriKitabi.Core.Books;
using BildiriKitabi.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace BildiriKitabi.Infrastructure.Persistence.Configurations;

internal sealed class BookConfiguration : IEntityTypeConfiguration<Book>
{
    private static readonly string StatusList = string.Join(", ", Enum.GetNames<BookStatus>().Select(n => $"'{n}'"));

    public void Configure(EntityTypeBuilder<Book> builder)
    {
        builder.ToTable("Kitaplar", table =>
        {
            table.HasCheckConstraint("CK_Kitaplar_IlerlemeYuzdesi", "[IlerlemeYuzdesi] BETWEEN 0 AND 100");
            table.HasCheckConstraint("CK_Kitaplar_Durum", $"[Durum] IN ({StatusList})");
            table.HasCheckConstraint("CK_Kitaplar_Tamamlandi_Pdf", "[Durum] <> 'Completed' OR [PdfDepolamaAnahtari] IS NOT NULL");
            table.HasCheckConstraint("CK_Kitaplar_Basarisiz_Mesaj", "[Durum] <> 'Failed' OR [HataMesaji] IS NOT NULL");
            table.HasCheckConstraint("CK_Kitaplar_Silinme", "([AktifMi] = 1 AND [SilinmeZamani] IS NULL) OR ([AktifMi] = 0 AND [SilinmeZamani] IS NOT NULL)");
        });

        // The single control that keeps deleted books out: every query that starts from Books (and every paper reached
        // through a book) sees active books only, so no code writes its own AktifMi condition. Only the deleted-books
        // list and restore step past it, deliberately, through BookQueryFilters.Deleted.
        builder.HasQueryFilter(BookQueryFilters.Active, b => b.IsActive);

        // Two identities: Id (int IDENTITY) is the clustered primary key and the foreign key target, Uid is the only id
        // that leaves the application (API, queue, logs, storage keys). Uid is filled by EF Core's
        // SequentialGuidValueGenerator when the book is added; Guid.CreateVersion7() is not used because SQL Server
        // orders uniqueidentifier starting from the last six bytes, so v7 values would land randomly in the index.
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("Id").UseIdentityColumn();
        builder.Property(b => b.Uid).HasColumnName("Uid").ValueGeneratedOnAdd().HasValueGenerator<SequentialGuidValueGenerator>();
        builder.HasIndex(b => b.Uid).IsUnique().HasDatabaseName("UX_Kitaplar_Uid");

        builder.Property(b => b.Name).HasColumnName("Ad").HasMaxLength(Book.NameMaxLength).IsRequired();
        builder.Property(b => b.Status).HasColumnName("Durum").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(b => b.Stage).HasColumnName("Asama").HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.ProgressPercent).HasColumnName("IlerlemeYuzdesi").HasColumnType("tinyint").HasDefaultValue((byte)0);
        builder.Property(b => b.ErrorCode).HasColumnName("HataKodu").HasMaxLength(Book.ErrorCodeMaxLength);
        builder.Property(b => b.ErrorMessage).HasColumnName("HataMesaji").HasMaxLength(Book.ErrorMessageMaxLength);
        builder.Property(b => b.PdfStorageKey).HasColumnName("PdfDepolamaAnahtari").HasMaxLength(Book.StorageKeyMaxLength);
        builder.Property(b => b.PdfSizeBytes).HasColumnName("PdfBoyutuBayt");
        builder.Property(b => b.PageCount).HasColumnName("SayfaSayisi");
        builder.Property(b => b.CreatedAt).HasColumnName("OlusturulmaZamani").HasDefaultValueSql("sysutcdatetime()");
        builder.Property(b => b.ProcessingStartedAt).HasColumnName("IslemBaslangicZamani");
        builder.Property(b => b.QueuedAt).HasColumnName("KuyrugaAlinmaZamani");
        builder.Property(b => b.ProcessingFinishedAt).HasColumnName("IslemBitisZamani");
        // The default serves rows written by hand; EF Core always sends the value, so false is never mistaken for "unset".
        builder.Property(b => b.IsActive).HasColumnName("AktifMi").HasDefaultValue(true).ValueGeneratedNever();
        builder.Property(b => b.DeletedAt).HasColumnName("SilinmeZamani");
        builder.Property(b => b.RowVersion).HasColumnName("SatirVersiyonu").IsRowVersion();

        builder.HasMany(b => b.Papers)
            .WithOne(p => p.Book)
            .HasForeignKey(p => p.BookId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(b => b.Papers).HasField("_papers").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(b => b.IsEditable);
        builder.Ignore(b => b.IsBusy);

        builder.HasIndex(b => b.Status).HasDatabaseName("IX_Kitaplar_Durum");
        builder.HasIndex(b => b.CreatedAt).IsDescending().HasDatabaseName("IX_Kitaplar_OlusturulmaZamani");
    }
}
