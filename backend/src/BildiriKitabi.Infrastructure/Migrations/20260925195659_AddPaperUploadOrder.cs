using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BildiriKitabi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaperUploadOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "YuklemeSirasi",
                table: "Bildiriler",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Papers uploaded before this column existed: their current position is the best record of the upload.
            // EXEC defers compilation, so the idempotent script also works in the batch that adds the column.
            migrationBuilder.Sql("EXEC(N'UPDATE [Bildiriler] SET [YuklemeSirasi] = [SiraNo]');");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Bildiriler_YuklemeSirasi",
                table: "Bildiriler",
                sql: "[YuklemeSirasi] >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Bildiriler_YuklemeSirasi",
                table: "Bildiriler");

            migrationBuilder.DropColumn(
                name: "YuklemeSirasi",
                table: "Bildiriler");
        }
    }
}
