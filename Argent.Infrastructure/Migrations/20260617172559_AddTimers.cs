using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argent.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTimers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowDefinitions_AspNetUsers_CreatedById",
                table: "WorkflowDefinitions");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowDefinitions_AspNetUsers_UpdatedById",
                table: "WorkflowDefinitions");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowDrafts_WorkflowDefinitions_WorkflowId",
                table: "WorkflowDrafts");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowVersions_WorkflowDefinitions_WorkflowId",
                table: "WorkflowVersions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WorkflowDefinitions",
                table: "WorkflowDefinitions");

            migrationBuilder.DropColumn(
                name: "DefinitionId",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "TokenPayload",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "WorkflowInstanceId",
                table: "WorkItems");

            migrationBuilder.RenameTable(
                name: "WorkflowDefinitions",
                newName: "Workflows");

            migrationBuilder.RenameIndex(
                name: "IX_WorkflowDefinitions_UpdatedById",
                table: "Workflows",
                newName: "IX_Workflows_UpdatedById");

            migrationBuilder.RenameIndex(
                name: "IX_WorkflowDefinitions_CreatedById",
                table: "Workflows",
                newName: "IX_Workflows_CreatedById");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Workflows",
                table: "Workflows",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "Timers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TriggerTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Timers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Timers_State_TriggerTime",
                table: "Timers",
                columns: new[] { "State", "TriggerTime" });

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowDrafts_Workflows_WorkflowId",
                table: "WorkflowDrafts",
                column: "WorkflowId",
                principalTable: "Workflows",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Workflows_AspNetUsers_CreatedById",
                table: "Workflows",
                column: "CreatedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Workflows_AspNetUsers_UpdatedById",
                table: "Workflows",
                column: "UpdatedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowVersions_Workflows_WorkflowId",
                table: "WorkflowVersions",
                column: "WorkflowId",
                principalTable: "Workflows",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowDrafts_Workflows_WorkflowId",
                table: "WorkflowDrafts");

            migrationBuilder.DropForeignKey(
                name: "FK_Workflows_AspNetUsers_CreatedById",
                table: "Workflows");

            migrationBuilder.DropForeignKey(
                name: "FK_Workflows_AspNetUsers_UpdatedById",
                table: "Workflows");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowVersions_Workflows_WorkflowId",
                table: "WorkflowVersions");

            migrationBuilder.DropTable(
                name: "Timers");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Workflows",
                table: "Workflows");

            migrationBuilder.RenameTable(
                name: "Workflows",
                newName: "WorkflowDefinitions");

            migrationBuilder.RenameIndex(
                name: "IX_Workflows_UpdatedById",
                table: "WorkflowDefinitions",
                newName: "IX_WorkflowDefinitions_UpdatedById");

            migrationBuilder.RenameIndex(
                name: "IX_Workflows_CreatedById",
                table: "WorkflowDefinitions",
                newName: "IX_WorkflowDefinitions_CreatedById");

            migrationBuilder.AddColumn<Guid>(
                name: "DefinitionId",
                table: "WorkItems",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "TokenPayload",
                table: "WorkItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowInstanceId",
                table: "WorkItems",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "PK_WorkflowDefinitions",
                table: "WorkflowDefinitions",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowDefinitions_AspNetUsers_CreatedById",
                table: "WorkflowDefinitions",
                column: "CreatedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowDefinitions_AspNetUsers_UpdatedById",
                table: "WorkflowDefinitions",
                column: "UpdatedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowDrafts_WorkflowDefinitions_WorkflowId",
                table: "WorkflowDrafts",
                column: "WorkflowId",
                principalTable: "WorkflowDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowVersions_WorkflowDefinitions_WorkflowId",
                table: "WorkflowVersions",
                column: "WorkflowId",
                principalTable: "WorkflowDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
