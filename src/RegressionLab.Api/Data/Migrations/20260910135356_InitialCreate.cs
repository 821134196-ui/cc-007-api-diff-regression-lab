using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegressionLab.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RuleSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IgnorePaths = table.Column<List<string>>(type: "jsonb", nullable: false),
                    ArraySortKeys = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: false),
                    NumericAbsTolerance = table.Column<double>(type: "double precision", nullable: false),
                    NumericRelTolerance = table.Column<double>(type: "double precision", nullable: false),
                    DynamicPatterns = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: false),
                    IgnoreHeaders = table.Column<List<string>>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RuleSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleSetVersion = table.Column<int>(type: "integer", nullable: false),
                    RuleSetName = table.Column<string>(type: "text", nullable: false),
                    RuleSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    BaselineBaseUrl = table.Column<string>(type: "text", nullable: false),
                    CandidateBaseUrl = table.Column<string>(type: "text", nullable: false),
                    TotalScenarios = table.Column<int>(type: "integer", nullable: false),
                    CompletedScenarios = table.Column<int>(type: "integer", nullable: false),
                    DiffCount = table.Column<int>(type: "integer", nullable: false),
                    NetworkFailureCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    options = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Scenarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    PathTemplate = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PathParameters = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: false),
                    QueryParameters = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: false),
                    Headers = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: false),
                    JsonBody = table.Column<string>(type: "text", nullable: true),
                    SecretRefs = table.Column<List<string>>(type: "jsonb", nullable: false),
                    RuleSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scenarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Scenarios_RuleSets_RuleSetId",
                        column: x => x.RuleSetId,
                        principalTable: "RuleSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RunResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioName = table.Column<string>(type: "text", nullable: false),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    Baseline = table.Column<string>(type: "jsonb", nullable: false),
                    Candidate = table.Column<string>(type: "jsonb", nullable: false),
                    Diffs = table.Column<string>(type: "jsonb", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunResults_Runs_RunId",
                        column: x => x.RunId,
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RuleSets_IsActive",
                table: "RuleSets",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_RuleSets_Name_Version",
                table: "RuleSets",
                columns: new[] { "Name", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunResults_RunId",
                table: "RunResults",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_Runs_CreatedAt",
                table: "Runs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Scenarios_Enabled",
                table: "Scenarios",
                column: "Enabled");

            migrationBuilder.CreateIndex(
                name: "IX_Scenarios_RuleSetId",
                table: "Scenarios",
                column: "RuleSetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunResults");

            migrationBuilder.DropTable(
                name: "Scenarios");

            migrationBuilder.DropTable(
                name: "Runs");

            migrationBuilder.DropTable(
                name: "RuleSets");
        }
    }
}
