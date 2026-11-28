using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniMan.Migrations
{
    /// <inheritdoc />
    public partial class DropUnusedGenreColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Removes CachedAnimeId/CachedMangaId (AM-087). SQLite cannot drop a column that
            // is part of an FK, so the table is rebuilt — by hand, because EF's own rebuild
            // turns foreign keys off and so runs outside the migration's transaction: an app
            // closed halfway would leave a catalog that fails every later start. Here every
            // statement is in the transaction. Nothing references MediaGenres, so dropping it
            // with foreign keys on deletes nothing elsewhere.
            migrationBuilder.Sql(
                """
                CREATE TABLE "ef_temp_MediaGenres" (
                    "MediaId" INTEGER NOT NULL,
                    "MediaType" TEXT NOT NULL,
                    "GenreId" INTEGER NOT NULL,
                    CONSTRAINT "PK_MediaGenres" PRIMARY KEY ("MediaId", "MediaType", "GenreId"),
                    CONSTRAINT "FK_MediaGenres_Genres_GenreId" FOREIGN KEY ("GenreId") REFERENCES "Genres" ("Id") ON DELETE CASCADE
                );
                """);
            migrationBuilder.Sql(
                """
                INSERT INTO "ef_temp_MediaGenres" ("MediaId", "MediaType", "GenreId")
                SELECT "MediaId", "MediaType", "GenreId" FROM "MediaGenres";
                """);
            migrationBuilder.Sql("""DROP TABLE "MediaGenres";""");
            migrationBuilder.Sql("""ALTER TABLE "ef_temp_MediaGenres" RENAME TO "MediaGenres";""");
            migrationBuilder.Sql("""CREATE INDEX "IX_MediaGenres_GenreId" ON "MediaGenres" ("GenreId");""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CachedAnimeId",
                table: "MediaGenres",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CachedMangaId",
                table: "MediaGenres",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaGenres_CachedAnimeId",
                table: "MediaGenres",
                column: "CachedAnimeId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaGenres_CachedMangaId",
                table: "MediaGenres",
                column: "CachedMangaId");

            migrationBuilder.AddForeignKey(
                name: "FK_MediaGenres_Anime_CachedAnimeId",
                table: "MediaGenres",
                column: "CachedAnimeId",
                principalTable: "Anime",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_MediaGenres_Manga_CachedMangaId",
                table: "MediaGenres",
                column: "CachedMangaId",
                principalTable: "Manga",
                principalColumn: "Id");
        }
    }
}
