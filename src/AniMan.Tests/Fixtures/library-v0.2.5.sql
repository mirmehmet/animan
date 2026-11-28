-- Frozen library.db schema as AniMan v0.2.5 created it (migration 20260726161715_InitialCreate,
-- unchanged since that release; generated with `dotnet ef migrations script 0 InitialCreate`).
-- Migration tests build the "old user database" from this file, not from the current EF
-- migrations or model (AM-091). Never edit it: it stands for files already on users' disks.

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "Settings" (
    "Key" TEXT NOT NULL CONSTRAINT "PK_Settings" PRIMARY KEY,
    "Value" TEXT NOT NULL
);

CREATE TABLE "TrackingStatuses" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_TrackingStatuses" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "IsDefault" INTEGER NOT NULL,
    "DisplayOrder" INTEGER NOT NULL,
    "Color" TEXT NULL
);

CREATE TABLE "LibraryItems" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_LibraryItems" PRIMARY KEY AUTOINCREMENT,
    "MalId" INTEGER NOT NULL,
    "MediaType" TEXT NOT NULL,
    "StatusId" INTEGER NOT NULL,
    "Score" INTEGER NULL,
    "IsFavorite" INTEGER NOT NULL,
    "RewatchCount" INTEGER NOT NULL,
    "LastRewatchDate" TEXT NULL,
    "StartedAt" TEXT NULL,
    "CompletedAt" TEXT NULL,
    "UserRating" TEXT NULL,
    "AddedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "FK_LibraryItems_TrackingStatuses_StatusId" FOREIGN KEY ("StatusId") REFERENCES "TrackingStatuses" ("Id") ON DELETE CASCADE
);

CREATE TABLE "ChapterProgress" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ChapterProgress" PRIMARY KEY AUTOINCREMENT,
    "LibraryItemId" INTEGER NOT NULL,
    "ChapterNumber" INTEGER NOT NULL,
    "IsRead" INTEGER NOT NULL,
    "ReadAt" TEXT NULL,
    CONSTRAINT "FK_ChapterProgress_LibraryItems_LibraryItemId" FOREIGN KEY ("LibraryItemId") REFERENCES "LibraryItems" ("Id") ON DELETE CASCADE
);

CREATE TABLE "EpisodeProgress" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_EpisodeProgress" PRIMARY KEY AUTOINCREMENT,
    "LibraryItemId" INTEGER NOT NULL,
    "EpisodeNumber" INTEGER NOT NULL,
    "IsWatched" INTEGER NOT NULL,
    "WatchedAt" TEXT NULL,
    CONSTRAINT "FK_EpisodeProgress_LibraryItems_LibraryItemId" FOREIGN KEY ("LibraryItemId") REFERENCES "LibraryItems" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Notes" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Notes" PRIMARY KEY AUTOINCREMENT,
    "LibraryItemId" INTEGER NOT NULL,
    "EpisodeNumber" INTEGER NULL,
    "ChapterNumber" INTEGER NULL,
    "Content" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_Notes_LibraryItems_LibraryItemId" FOREIGN KEY ("LibraryItemId") REFERENCES "LibraryItems" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Snapshots" (
    "LibraryItemId" INTEGER NOT NULL CONSTRAINT "PK_Snapshots" PRIMARY KEY,
    "Title" TEXT NOT NULL,
    "TitleJapanese" TEXT NULL,
    "Synopsis" TEXT NULL,
    "Type" TEXT NULL,
    "Status" TEXT NULL,
    "Studio" TEXT NULL,
    "TotalEpisodes" INTEGER NULL,
    "TotalChapters" INTEGER NULL,
    "TotalVolumes" INTEGER NULL,
    "AiringStart" TEXT NULL,
    "AiringEnd" TEXT NULL,
    "Season" TEXT NULL,
    "Year" INTEGER NULL,
    "MalScore" REAL NULL,
    "Genres" TEXT NOT NULL,
    "CoverLocalPath" TEXT NULL,
    "CoverOriginalUrl" TEXT NULL,
    "StreamingPlatforms" TEXT NULL,
    "SnapshotAt" TEXT NOT NULL,
    CONSTRAINT "FK_Snapshots_LibraryItems_LibraryItemId" FOREIGN KEY ("LibraryItemId") REFERENCES "LibraryItems" ("Id") ON DELETE CASCADE
);

CREATE TABLE "StreamingOverrides" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_StreamingOverrides" PRIMARY KEY AUTOINCREMENT,
    "LibraryItemId" INTEGER NOT NULL,
    "PlatformName" TEXT NOT NULL,
    "Url" TEXT NULL,
    "AddedAt" TEXT NOT NULL,
    CONSTRAINT "FK_StreamingOverrides_LibraryItems_LibraryItemId" FOREIGN KEY ("LibraryItemId") REFERENCES "LibraryItems" ("Id") ON DELETE CASCADE
);

INSERT INTO "TrackingStatuses" ("Id", "Color", "DisplayOrder", "IsDefault", "Name")
VALUES (1, NULL, 1, 1, 'Watching');
SELECT changes();

INSERT INTO "TrackingStatuses" ("Id", "Color", "DisplayOrder", "IsDefault", "Name")
VALUES (2, NULL, 1, 1, 'Reading');
SELECT changes();

INSERT INTO "TrackingStatuses" ("Id", "Color", "DisplayOrder", "IsDefault", "Name")
VALUES (3, NULL, 2, 1, 'Completed');
SELECT changes();

INSERT INTO "TrackingStatuses" ("Id", "Color", "DisplayOrder", "IsDefault", "Name")
VALUES (4, NULL, 3, 1, 'On-hold');
SELECT changes();

INSERT INTO "TrackingStatuses" ("Id", "Color", "DisplayOrder", "IsDefault", "Name")
VALUES (5, NULL, 4, 1, 'Dropped');
SELECT changes();

INSERT INTO "TrackingStatuses" ("Id", "Color", "DisplayOrder", "IsDefault", "Name")
VALUES (6, NULL, 5, 1, 'Plan to watch');
SELECT changes();

INSERT INTO "TrackingStatuses" ("Id", "Color", "DisplayOrder", "IsDefault", "Name")
VALUES (7, NULL, 5, 1, 'Plan to read');
SELECT changes();


CREATE UNIQUE INDEX "IX_ChapterProgress_LibraryItemId_ChapterNumber" ON "ChapterProgress" ("LibraryItemId", "ChapterNumber");

CREATE UNIQUE INDEX "IX_EpisodeProgress_LibraryItemId_EpisodeNumber" ON "EpisodeProgress" ("LibraryItemId", "EpisodeNumber");

CREATE UNIQUE INDEX "IX_LibraryItems_MalId_MediaType" ON "LibraryItems" ("MalId", "MediaType");

CREATE INDEX "IX_LibraryItems_StatusId" ON "LibraryItems" ("StatusId");

CREATE INDEX "IX_Notes_LibraryItemId" ON "Notes" ("LibraryItemId");

CREATE INDEX "IX_StreamingOverrides_LibraryItemId" ON "StreamingOverrides" ("LibraryItemId");

CREATE UNIQUE INDEX "IX_TrackingStatuses_Name" ON "TrackingStatuses" ("Name");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260726161715_InitialCreate', '9.0.21');

COMMIT;

