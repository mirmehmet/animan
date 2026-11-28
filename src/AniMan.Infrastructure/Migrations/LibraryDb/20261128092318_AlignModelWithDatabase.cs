using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AniMan.Migrations.LibraryDb
{
    /// <inheritdoc />
    public partial class AlignModelWithDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only adds an index. The model's status FK moved from Cascade to Restrict (AM-048)
            // with no database change on purpose: SQLite can only change an FK by rebuilding
            // LibraryItems, and the trigger TR_TrackingStatuses_RestrictInUse already refuses
            // to delete a status in use.
            migrationBuilder.CreateIndex(
                name: "IX_LibraryItems_MediaType_DeletedAt",
                table: "LibraryItems",
                columns: new[] { "MediaType", "DeletedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LibraryItems_MediaType_DeletedAt",
                table: "LibraryItems");
        }
    }
}
