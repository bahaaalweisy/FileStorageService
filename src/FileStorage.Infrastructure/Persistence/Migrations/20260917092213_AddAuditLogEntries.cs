using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileStorage.Infrastructure.Persistence.Migrations
{

    public partial class AddAuditLogEntries : Migration
    {

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorRole = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResourceId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ResourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_Actor",
                table: "AuditLogEntries",
                columns: new[] { "ActorUserId", "TimestampUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_Operation",
                table: "AuditLogEntries",
                columns: new[] { "Operation", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_Resource",
                table: "AuditLogEntries",
                columns: new[] { "ResourceId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_Timestamp",
                table: "AuditLogEntries",
                columns: new[] { "TimestampUtc", "Id" });
        }


        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogEntries");
        }
    }
}
