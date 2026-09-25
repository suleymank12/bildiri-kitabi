using BildiriKitabi.Core.Books;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

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
        });

        // uniqueidentifier filled by EF Core's default SequentialGuidValueGenerator for SQL Server; Guid.CreateVersion7()
        // is not used because SQL Server orders uniqueidentifier starting from the last six bytes, so v7 values would
        // land randomly in the clustered index and fragment it.
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("Id").ValueGeneratedOnAdd();

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
        builder.Property(b => b.ProcessingFinishedAt).HasColumnName("IslemBitisZamani");
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
