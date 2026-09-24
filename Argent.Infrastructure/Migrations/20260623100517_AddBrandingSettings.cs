using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argent.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrandingSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SiteName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LogoUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    FaviconUrl = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    PrimaryColor = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PrimaryHoverColor = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FooterText = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CustomCss = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandingSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrandingSettings");
        }
    }
}
