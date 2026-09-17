using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileStorage.Infrastructure.Persistence.Migrations
{

    public partial class AddUploadSessions : Migration
    {

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UploadSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Tags = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    TotalSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ReceivedBytes = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinalizedFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UploadSessions_Owner_Status",
                table: "UploadSessions",
                columns: new[] { "OwnerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_UploadSessions_Status_Expiry",
                table: "UploadSessions",
                columns: new[] { "Status", "ExpiresAtUtc" });
        }


        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UploadSessions");
        }
    }
}
