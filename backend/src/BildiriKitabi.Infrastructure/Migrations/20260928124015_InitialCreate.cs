using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BildiriKitabi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Kitaplar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ad = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Durum = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Asama = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    IlerlemeYuzdesi = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)0),
                    HataKodu = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    HataMesaji = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PdfDepolamaAnahtari = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    PdfBoyutuBayt = table.Column<long>(type: "bigint", nullable: true),
                    SayfaSayisi = table.Column<int>(type: "int", nullable: true),
                    OlusturulmaZamani = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "sysutcdatetime()"),
                    IslemBaslangicZamani = table.Column<DateTime>(type: "datetime2", nullable: true),
                    KuyrugaAlinmaZamani = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IslemBitisZamani = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SatirVersiyonu = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kitaplar", x => x.Id);
                    table.CheckConstraint("CK_Kitaplar_Basarisiz_Mesaj", "[Durum] <> 'Failed' OR [HataMesaji] IS NOT NULL");
                    table.CheckConstraint("CK_Kitaplar_Durum", "[Durum] IN ('Uploaded', 'Queued', 'Processing', 'Completed', 'Failed')");
                    table.CheckConstraint("CK_Kitaplar_IlerlemeYuzdesi", "[IlerlemeYuzdesi] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_Kitaplar_Tamamlandi_Pdf", "[Durum] <> 'Completed' OR [PdfDepolamaAnahtari] IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "Bildiriler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KitapId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiraNo = table.Column<int>(type: "int", nullable: false),
                    YuklemeSirasi = table.Column<int>(type: "int", nullable: false),
                    OrijinalDosyaAdi = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    DepolamaAnahtari = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    DosyaBoyutuBayt = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    Baslik = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    BaslikKaynagi = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BaslangicSayfasi = table.Column<int>(type: "int", nullable: true),
                    BitisSayfasi = table.Column<int>(type: "int", nullable: true),
                    SilinenEpostaSayisi = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    SilinenTelefonSayisi = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    YuklenmeZamani = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bildiriler", x => x.Id);
                    table.CheckConstraint("CK_Bildiriler_SiraNo", "[SiraNo] >= 1");
                    table.CheckConstraint("CK_Bildiriler_YuklemeSirasi", "[YuklemeSirasi] >= 1");
                    table.ForeignKey(
                        name: "FK_Bildiriler_Kitaplar_KitapId",
                        column: x => x.KitapId,
                        principalTable: "Kitaplar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Bildiriler_KitapId_Sha256",
                table: "Bildiriler",
                columns: new[] { "KitapId", "Sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Bildiriler_KitapId_SiraNo",
                table: "Bildiriler",
                columns: new[] { "KitapId", "SiraNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Kitaplar_Durum",
                table: "Kitaplar",
                column: "Durum");

            migrationBuilder.CreateIndex(
                name: "IX_Kitaplar_OlusturulmaZamani",
                table: "Kitaplar",
                column: "OlusturulmaZamani",
                descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bildiriler");

            migrationBuilder.DropTable(
                name: "Kitaplar");
        }
    }
}
