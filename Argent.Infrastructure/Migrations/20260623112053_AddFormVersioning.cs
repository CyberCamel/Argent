using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argent.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFormVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create the new tables first
            migrationBuilder.CreateTable(
                name: "FormDesignDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormDesignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Definition = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormDesignDrafts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormDesignVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormDesignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Definition = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormDesignVersions", x => x.Id);
                });

            // 2. Migrate existing definitions into drafts (skip rows with NULL definition)
            migrationBuilder.Sql("""
                INSERT INTO FormDesignDrafts (Id, FormDesignId, Definition, CreatedAt, UpdatedAt, UpdatedBy)
                SELECT NEWID(), Id, Definition, GETUTCDATE(), GETUTCDATE(), 'migration'
                FROM FormDesigns
                WHERE Definition IS NOT NULL;
                """);

            // 3. Now safe to drop the source column
            migrationBuilder.DropColumn(
                name: "Definition",
                table: "FormDesigns");

            migrationBuilder.CreateIndex(
                name: "IX_FormDesignDrafts_FormDesignId",
                table: "FormDesignDrafts",
                column: "FormDesignId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormDesignVersions_FormDesignId_Version",
                table: "FormDesignVersions",
                columns: new[] { "FormDesignId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormDesignDrafts");

            migrationBuilder.DropTable(
                name: "FormDesignVersions");

            migrationBuilder.AddColumn<string>(
                name: "Definition",
                table: "FormDesigns",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
