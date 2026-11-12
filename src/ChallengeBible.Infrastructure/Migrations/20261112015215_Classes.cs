using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Classes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClassTerm",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassTerm", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ClassTerm_Tenant_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "app",
                        principalTable: "Tenant",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Class",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TermId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsNewcomer = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Class", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_Class_ClassTerm_TenantId_TermId",
                        columns: x => new { x.TenantId, x.TermId },
                        principalSchema: "app",
                        principalTable: "ClassTerm",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ClassMember",
                schema: "app",
                columns: table => new
                {
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TermId = table.Column<int>(type: "int", nullable: false),
                    ClassId = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    IsTeacher = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassMember", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_ClassMember_Class_TenantId_ClassId",
                        columns: x => new { x.TenantId, x.ClassId },
                        principalSchema: "app",
                        principalTable: "Class",
                        principalColumns: new[] { "TenantId", "Id" });
                    table.ForeignKey(
                        name: "FK_ClassMember_Person_TenantId_PersonId",
                        columns: x => new { x.TenantId, x.PersonId },
                        principalSchema: "app",
                        principalTable: "Person",
                        principalColumns: new[] { "TenantId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Class_TenantId_TermId",
                schema: "app",
                table: "Class",
                columns: new[] { "TenantId", "TermId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassMember_TenantId_ClassId",
                schema: "app",
                table: "ClassMember",
                columns: new[] { "TenantId", "ClassId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassMember_TenantId_PersonId",
                schema: "app",
                table: "ClassMember",
                columns: new[] { "TenantId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClassMember_TenantId_TermId_PersonId",
                schema: "app",
                table: "ClassMember",
                columns: new[] { "TenantId", "TermId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClassTerm_TenantId_Year",
                schema: "app",
                table: "ClassTerm",
                columns: new[] { "TenantId", "Year" },
                unique: true);

            // EXEC: keep the batch from checking tables before they exist (real DB 33268)
            migrationBuilder.Sql("EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.ClassTerm, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.ClassTerm AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.ClassTerm AFTER UPDATE;')");
            migrationBuilder.Sql("EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.Class, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Class AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.Class AFTER UPDATE;')");
            migrationBuilder.Sql("EXEC(N'ALTER SECURITY POLICY sec.TenantIsolation ADD FILTER PREDICATE sec.fn_Tenant(TenantId) ON app.ClassMember, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.ClassMember AFTER INSERT, ADD BLOCK PREDICATE sec.fn_Tenant(TenantId) ON app.ClassMember AFTER UPDATE;')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation DROP FILTER PREDICATE ON app.ClassTerm, DROP BLOCK PREDICATE ON app.ClassTerm AFTER INSERT, DROP BLOCK PREDICATE ON app.ClassTerm AFTER UPDATE;");
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation DROP FILTER PREDICATE ON app.Class, DROP BLOCK PREDICATE ON app.Class AFTER INSERT, DROP BLOCK PREDICATE ON app.Class AFTER UPDATE;");
            migrationBuilder.Sql("ALTER SECURITY POLICY sec.TenantIsolation DROP FILTER PREDICATE ON app.ClassMember, DROP BLOCK PREDICATE ON app.ClassMember AFTER INSERT, DROP BLOCK PREDICATE ON app.ClassMember AFTER UPDATE;");

            migrationBuilder.DropTable(
                name: "ClassMember",
                schema: "app");

            migrationBuilder.DropTable(
                name: "Class",
                schema: "app");

            migrationBuilder.DropTable(
                name: "ClassTerm",
                schema: "app");
        }
    }
}
