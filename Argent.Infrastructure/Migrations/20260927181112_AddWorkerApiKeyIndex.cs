using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argent.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerApiKeyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Workers_ApiKeyHash",
                table: "Workers",
                column: "ApiKeyHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Workers_ApiKeyHash",
                table: "Workers");
        }
    }
}
