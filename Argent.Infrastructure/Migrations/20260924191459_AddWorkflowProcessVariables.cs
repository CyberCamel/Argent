using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argent.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowProcessVariables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProcessVariablesJson",
                table: "WorkflowInstances",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcessVariablesJson",
                table: "WorkflowInstances");
        }
    }
}
