using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BildiriKitabi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookQueuedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "KuyrugaAlinmaZamani",
                table: "Kitaplar",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KuyrugaAlinmaZamani",
                table: "Kitaplar");
        }
    }
}
