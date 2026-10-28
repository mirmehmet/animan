using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniMan.Migrations
{
    /// <inheritdoc />
    public partial class HardenCatalogIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM "AnimeStreaming"
                WHERE "Id" NOT IN (
                    SELECT MAX("Id") FROM "AnimeStreaming" GROUP BY "AnimeId", "PlatformName"
                );
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM "Episodes"
                WHERE "Id" NOT IN (
                    SELECT MAX("Id") FROM "Episodes" GROUP BY "AnimeId", "EpisodeNumber"
                );
                """);

            migrationBuilder.DropIndex(
                name: "IX_AnimeStreaming_AnimeId",
                table: "AnimeStreaming");

            migrationBuilder.DropIndex(
                name: "IX_Episodes_AnimeId",
                table: "Episodes");

            migrationBuilder.CreateIndex(
                name: "IX_AnimeStreaming_AnimeId_PlatformName",
                table: "AnimeStreaming",
                columns: new[] { "AnimeId", "PlatformName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_AnimeId_EpisodeNumber",
                table: "Episodes",
                columns: new[] { "AnimeId", "EpisodeNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AnimeStreaming_AnimeId_PlatformName",
                table: "AnimeStreaming");

            migrationBuilder.DropIndex(
                name: "IX_Episodes_AnimeId_EpisodeNumber",
                table: "Episodes");

            migrationBuilder.CreateIndex(
                name: "IX_AnimeStreaming_AnimeId",
                table: "AnimeStreaming",
                column: "AnimeId");

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_AnimeId",
                table: "Episodes",
                column: "AnimeId");
        }
    }
}
