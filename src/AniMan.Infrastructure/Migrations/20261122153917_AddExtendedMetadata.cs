using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniMan.Migrations
{
    /// <inheritdoc />
    public partial class AddExtendedMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Authors",
                table: "Manga",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DataVersion",
                table: "Manga",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "Demographics",
                table: "Manga",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Themes",
                table: "Manga",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TitleEnglish",
                table: "Manga",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgeRating",
                table: "Anime",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DataVersion",
                table: "Anime",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "Demographics",
                table: "Anime",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                table: "Anime",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Anime",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Themes",
                table: "Anime",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TitleEnglish",
                table: "Anime",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Authors",
                table: "Manga");

            migrationBuilder.DropColumn(
                name: "DataVersion",
                table: "Manga");

            migrationBuilder.DropColumn(
                name: "Demographics",
                table: "Manga");

            migrationBuilder.DropColumn(
                name: "Themes",
                table: "Manga");

            migrationBuilder.DropColumn(
                name: "TitleEnglish",
                table: "Manga");

            migrationBuilder.DropColumn(
                name: "AgeRating",
                table: "Anime");

            migrationBuilder.DropColumn(
                name: "DataVersion",
                table: "Anime");

            migrationBuilder.DropColumn(
                name: "Demographics",
                table: "Anime");

            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "Anime");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Anime");

            migrationBuilder.DropColumn(
                name: "Themes",
                table: "Anime");

            migrationBuilder.DropColumn(
                name: "TitleEnglish",
                table: "Anime");
        }
    }
}
