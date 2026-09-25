using BildiriKitabi.Core.Books;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BildiriKitabi.Infrastructure.Persistence.Configurations;

internal sealed class PaperConfiguration : IEntityTypeConfiguration<Paper>
{
    public void Configure(EntityTypeBuilder<Paper> builder)
    {
        builder.ToTable("Bildiriler", table => table.HasCheckConstraint("CK_Bildiriler_SiraNo", "[SiraNo] >= 1"));

        // Same sequential GUID strategy as Kitaplar (see BookConfiguration).
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("Id").ValueGeneratedOnAdd();

        builder.Property(p => p.BookId).HasColumnName("KitapId");
        builder.Property(p => p.Order).HasColumnName("SiraNo");
        builder.Property(p => p.OriginalFileName).HasColumnName("OrijinalDosyaAdi").HasMaxLength(Paper.FileNameMaxLength).IsRequired();
        builder.Property(p => p.StorageKey).HasColumnName("DepolamaAnahtari").HasMaxLength(Book.StorageKeyMaxLength).IsRequired();
        builder.Property(p => p.SizeBytes).HasColumnName("DosyaBoyutuBayt");
        builder.Property(p => p.Sha256).HasColumnName("Sha256").HasColumnType("binary(32)").HasMaxLength(Paper.Sha256Length).IsFixedLength().IsRequired();
        builder.Property(p => p.Title).HasColumnName("Baslik").HasMaxLength(Paper.TitleMaxLength).IsRequired();
        builder.Property(p => p.TitleSource).HasColumnName("BaslikKaynagi").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.StartPage).HasColumnName("BaslangicSayfasi");
        builder.Property(p => p.EndPage).HasColumnName("BitisSayfasi");
        builder.Property(p => p.RemovedEmailCount).HasColumnName("SilinenEpostaSayisi").HasDefaultValue(0);
        builder.Property(p => p.RemovedPhoneCount).HasColumnName("SilinenTelefonSayisi").HasDefaultValue(0);
        builder.Property(p => p.UploadedAt).HasColumnName("YuklenmeZamani");

        builder.HasIndex(p => new { p.BookId, p.Order }).IsUnique().HasDatabaseName("UX_Bildiriler_KitapId_SiraNo");
        builder.HasIndex(p => new { p.BookId, p.Sha256 }).IsUnique().HasDatabaseName("UX_Bildiriler_KitapId_Sha256");
    }
}
