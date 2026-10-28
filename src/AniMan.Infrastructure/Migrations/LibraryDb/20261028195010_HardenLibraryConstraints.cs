using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniMan.Migrations.LibraryDb
{
    /// <inheritdoc />
    public partial class HardenLibraryConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM "Notes" WHERE TRIM("Content") = '';
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM "Notes"
                WHERE "Id" NOT IN (
                    SELECT MAX("Id") FROM "Notes"
                    GROUP BY "LibraryItemId", IFNULL("EpisodeNumber", -1), IFNULL("ChapterNumber", -1)
                );
                """);

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Notes_UniqueTarget"
                    ON "Notes" ("LibraryItemId", IFNULL("EpisodeNumber", -1), IFNULL("ChapterNumber", -1));
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER IF NOT EXISTS "TR_TrackingStatuses_RestrictInUse"
                BEFORE DELETE ON "TrackingStatuses"
                BEGIN
                    SELECT RAISE(ABORT, 'FOREIGN KEY constraint failed')
                    WHERE EXISTS (SELECT 1 FROM "LibraryItems" WHERE "StatusId" = OLD."Id");
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "TR_TrackingStatuses_RestrictInUse";""");
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_Notes_UniqueTarget";""");
        }
    }
}

