using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argent.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WorkersProvisionedByAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "Workers");

            migrationBuilder.AlterColumn<string>(
                name: "Runtime",
                table: "Workers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(byte),
                oldType: "tinyint");

            migrationBuilder.AddColumn<int>(
                name: "KeyRotations",
                table: "Workers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Runtime used to be a small enum integer and is now a free-form string the client
            // reports. The converted digits are meaningless in the new model, where a null means
            // "the client has not reported yet", so clear them rather than leave "0" on display.
            migrationBuilder.Sql(@"
                UPDATE [Workers] SET [Runtime] = NULL WHERE [Runtime] IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KeyRotations",
                table: "Workers");

            migrationBuilder.AlterColumn<byte>(
                name: "Runtime",
                table: "Workers",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Version",
                table: "Workers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }
    }
}
