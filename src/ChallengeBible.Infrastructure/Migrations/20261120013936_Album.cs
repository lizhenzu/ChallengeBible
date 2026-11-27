using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Album : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Album",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CoverPhotoId = table.Column<int>(type: "int", nullable: true),
                    Visibility = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    AllowDownload = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    CreatedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false),
                    UpdatedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Album", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_Album_Visibility", "[Visibility] IN ('Public', 'Login', 'Dept', 'Admin')");
                    table.ForeignKey(
                        name: "FK_Album_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Photo",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AlbumId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StoredName = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    ThumbName = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false),
                    ContentType = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    Hash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    UploadedByUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UploadedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Photo", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Photo_Album_TenantId_AlbumId",
                        columns: x => new { x.TenantId, x.AlbumId },
                        principalSchema: "app",
                        principalTable: "Album",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Album_TenantId_EventDate",
                schema: "app",
                table: "Album",
                columns: new[] { "TenantId", "EventDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Photo_TenantId_AlbumId_Hash",
                schema: "app",
                table: "Photo",
                columns: new[] { "TenantId", "AlbumId", "Hash" });
            // 부서 격리(RLS, ADR 0118): 다른 부서 앨범 · 사진은 읽지도 쓰지도 못한다
            migrationBuilder.Sql("EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.Album, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Album AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Album AFTER UPDATE, ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.Photo, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Photo AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Photo AFTER UPDATE;')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation DROP FILTER PREDICATE ON app.Album, DROP BLOCK PREDICATE ON app.Album AFTER INSERT, DROP BLOCK PREDICATE ON app.Album AFTER UPDATE, DROP FILTER PREDICATE ON app.Photo, DROP BLOCK PREDICATE ON app.Photo AFTER INSERT, DROP BLOCK PREDICATE ON app.Photo AFTER UPDATE;");
            migrationBuilder.DropTable(
                name: "Photo",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Album",
                schema: "app");
        }
    }
}
