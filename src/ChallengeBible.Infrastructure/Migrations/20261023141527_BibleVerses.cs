using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ChallengeBible.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BibleVerses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BibleTranslation",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<byte>(type: "tinyint", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SortOrder = table.Column<byte>(type: "tinyint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BibleTranslation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BibleVerse",
                schema: "app",
                columns: table => new
                {
                    TranslationId = table.Column<byte>(type: "tinyint", nullable: false),
                    BookId = table.Column<byte>(type: "tinyint", nullable: false),
                    Chapter = table.Column<short>(type: "smallint", nullable: false),
                    Verse = table.Column<short>(type: "smallint", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BibleVerse", x => new { x.TranslationId, x.BookId, x.Chapter, x.Verse });
                    table.ForeignKey(
                        name: "FK_BibleVerse_Book",
                        column: x => x.BookId,
                        principalSchema: "app",
                        principalTable: "BibleBook",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_BibleVerse_Translation",
                        column: x => x.TranslationId,
                        principalSchema: "app",
                        principalTable: "BibleTranslation",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                schema: "app",
                table: "BibleTranslation",
                columns: new[] { "Id", "Code", "Name", "SortOrder" },
                values: new object[,]
                {
                    { (byte)1, "KRV", "개역개정", (byte)1 },
                    { (byte)2, "KLB", "현대인의성경", (byte)2 },
                    { (byte)3, "NIV", "NIV", (byte)3 }
                });

            migrationBuilder.CreateIndex(
                name: "UQ_BibleTranslation_Code",
                schema: "app",
                table: "BibleTranslation",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BibleVerse_BookId",
                schema: "app",
                table: "BibleVerse",
                column: "BookId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BibleVerse",
                schema: "app");

            migrationBuilder.DropTable(
                name: "BibleTranslation",
                schema: "app");
        }
    }
}
