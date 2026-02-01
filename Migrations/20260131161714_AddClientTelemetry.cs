using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftManager.Migrations
{
    /// <inheritdoc />
    public partial class AddClientTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientAnalyticsEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventType = table.Column<string>(type: "TEXT", nullable: false),
                    EventData = table.Column<string>(type: "TEXT", nullable: true),
                    UserIdHash = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", nullable: true),
                    PageUrl = table.Column<string>(type: "TEXT", nullable: false),
                    UserAgent = table.Column<string>(type: "TEXT", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientAnalyticsEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClientErrors",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    StackTrace = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", nullable: true),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    ColumnNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    ErrorType = table.Column<string>(type: "TEXT", nullable: true),
                    PageUrl = table.Column<string>(type: "TEXT", nullable: false),
                    UserAgent = table.Column<string>(type: "TEXT", nullable: true),
                    UserIdHash = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BrowserInfo = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientErrors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PerformanceMetrics",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MetricName = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<double>(type: "REAL", nullable: false),
                    Rating = table.Column<string>(type: "TEXT", nullable: true),
                    PageUrl = table.Column<string>(type: "TEXT", nullable: false),
                    UserAgent = table.Column<string>(type: "TEXT", nullable: true),
                    ConnectionType = table.Column<string>(type: "TEXT", nullable: true),
                    EffectiveType = table.Column<string>(type: "TEXT", nullable: true),
                    DeviceMemory = table.Column<double>(type: "REAL", nullable: true),
                    HardwareConcurrency = table.Column<int>(type: "INTEGER", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BrowserInfo = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerformanceMetrics", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientAnalyticsEvents_EventType_Timestamp",
                table: "ClientAnalyticsEvents",
                columns: new[] { "EventType", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientAnalyticsEvents_SessionId",
                table: "ClientAnalyticsEvents",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientAnalyticsEvents_Timestamp",
                table: "ClientAnalyticsEvents",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_ClientAnalyticsEvents_UserIdHash",
                table: "ClientAnalyticsEvents",
                column: "UserIdHash");

            migrationBuilder.CreateIndex(
                name: "IX_ClientErrors_ErrorType_Timestamp",
                table: "ClientErrors",
                columns: new[] { "ErrorType", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientErrors_PageUrl",
                table: "ClientErrors",
                column: "PageUrl");

            migrationBuilder.CreateIndex(
                name: "IX_ClientErrors_Timestamp",
                table: "ClientErrors",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_ClientErrors_UserIdHash",
                table: "ClientErrors",
                column: "UserIdHash");

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceMetrics_MetricName_PageUrl",
                table: "PerformanceMetrics",
                columns: new[] { "MetricName", "PageUrl" });

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceMetrics_MetricName_Rating",
                table: "PerformanceMetrics",
                columns: new[] { "MetricName", "Rating" });

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceMetrics_MetricName_Timestamp",
                table: "PerformanceMetrics",
                columns: new[] { "MetricName", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceMetrics_Timestamp",
                table: "PerformanceMetrics",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientAnalyticsEvents");

            migrationBuilder.DropTable(
                name: "ClientErrors");

            migrationBuilder.DropTable(
                name: "PerformanceMetrics");
        }
    }
}
