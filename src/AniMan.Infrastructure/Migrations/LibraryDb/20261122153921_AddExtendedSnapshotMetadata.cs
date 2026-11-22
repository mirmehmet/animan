using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniMan.Migrations.LibraryDb
{
    /// <inheritdoc />
    public partial class AddExtendedSnapshotMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgeRating",
                table: "Snapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Authors",
                table: "Snapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DataVersion",
                table: "Snapshots",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "Demographics",
                table: "Snapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                table: "Snapshots",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Snapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Themes",
                table: "Snapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TitleEnglish",
                table: "Snapshots",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgeRating",
                table: "Snapshots");

            migrationBuilder.DropColumn(
                name: "Authors",
                table: "Snapshots");

            migrationBuilder.DropColumn(
                name: "DataVersion",
                table: "Snapshots");

            migrationBuilder.DropColumn(
                name: "Demographics",
                table: "Snapshots");

            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "Snapshots");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Snapshots");

            migrationBuilder.DropColumn(
                name: "Themes",
                table: "Snapshots");

            migrationBuilder.DropColumn(
                name: "TitleEnglish",
                table: "Snapshots");
        }
    }
}
