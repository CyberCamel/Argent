using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Argent.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkerRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkerName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Parameters = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    State = table.Column<byte>(type: "tinyint", nullable: false),
                    Priority = table.Column<short>(type: "smallint", nullable: false),
                    Attempt = table.Column<byte>(type: "tinyint", nullable: false),
                    MaxAttempts = table.Column<byte>(type: "tinyint", nullable: false),
                    LeaseExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClaimedByWorkerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClaimedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Outputs = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TimeoutSeconds = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Workers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Runtime = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Endpoint = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    ApiKeyHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Subjects = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Concurrency = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    InFlight = table.Column<int>(type: "int", nullable: false),
                    CurrentSubject = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LastHeartbeatAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RegisteredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkerRequests_Claim",
                table: "WorkerRequests",
                columns: new[] { "WorkerName", "State", "Priority", "CreatedAt" },
                filter: "[State] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerRequests_ClaimedByWorkerId",
                table: "WorkerRequests",
                column: "ClaimedByWorkerId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerRequests_InstanceId",
                table: "WorkerRequests",
                column: "InstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerRequests_Lease",
                table: "WorkerRequests",
                columns: new[] { "State", "LeaseExpiresAt" },
                filter: "[State] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_WorkerRequests_TokenId_NodeId",
                table: "WorkerRequests",
                columns: new[] { "TokenId", "NodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Workers_LastHeartbeatAt",
                table: "Workers",
                column: "LastHeartbeatAt");

            migrationBuilder.CreateIndex(
                name: "IX_Workers_Name",
                table: "Workers",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkerRequests");

            migrationBuilder.DropTable(
                name: "Workers");
        }
    }
}
