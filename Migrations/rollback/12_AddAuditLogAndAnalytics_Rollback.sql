BEGIN TRANSACTION;
CREATE TABLE "AuditLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AuditLogs" PRIMARY KEY AUTOINCREMENT,
    "CompanyId" INTEGER NOT NULL,
    "UserId" INTEGER NULL,
    "UserEmail" TEXT NOT NULL,
    "UserDisplayName" TEXT NOT NULL,
    "Action" TEXT NOT NULL,
    "EntityType" TEXT NOT NULL,
    "EntityId" INTEGER NULL,
    "Description" TEXT NOT NULL,
    "Details" TEXT NULL,
    "Timestamp" TEXT NOT NULL,
    "IpAddress" TEXT NOT NULL,
    "UserAgent" TEXT NOT NULL,
    CONSTRAINT "FK_AuditLogs_Companies_CompanyId" FOREIGN KEY ("CompanyId") REFERENCES "Companies" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_AuditLogs_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);

CREATE INDEX "IX_AuditLogs_CompanyId_Action" ON "AuditLogs" ("CompanyId", "Action");

CREATE INDEX "IX_AuditLogs_CompanyId_Timestamp" ON "AuditLogs" ("CompanyId", "Timestamp");

CREATE INDEX "IX_AuditLogs_CompanyId_UserId_Timestamp" ON "AuditLogs" ("CompanyId", "UserId", "Timestamp");

CREATE INDEX "IX_AuditLogs_UserId" ON "AuditLogs" ("UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20251019195032_AddAuditLogAndAnalytics', '9.0.9');

COMMIT;

