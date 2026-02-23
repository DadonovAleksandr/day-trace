using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DayTrace.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHighlightEventId_DropPeriodJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "period_jobs");

            migrationBuilder.DropTable(
                name: "period_run_counters");

            migrationBuilder.DropTable(
                name: "prompt_deliveries");

            migrationBuilder.AddColumn<Guid>(
                name: "highlight_event_id",
                table: "summaries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_summaries_highlight_event_id",
                table: "summaries",
                column: "highlight_event_id");

            migrationBuilder.AddForeignKey(
                name: "FK_summaries_events_highlight_event_id",
                table: "summaries",
                column: "highlight_event_id",
                principalTable: "events",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_summaries_events_highlight_event_id",
                table: "summaries");

            migrationBuilder.DropIndex(
                name: "IX_summaries_highlight_event_id",
                table: "summaries");

            migrationBuilder.DropColumn(
                name: "highlight_event_id",
                table: "summaries");

            migrationBuilder.CreateTable(
                name: "period_jobs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reconciled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    recovery_source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    run_number = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    target_summary_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_period_jobs", x => x.id);
                    table.ForeignKey(
                        name: "FK_period_jobs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "period_run_counters",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    last_run_number = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_period_run_counters", x => x.id);
                    table.ForeignKey(
                        name: "FK_period_run_counters_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "prompt_deliveries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    prompt_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prompt_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "FK_prompt_deliveries_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_period_jobs_idempotency_key",
                table: "period_jobs",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_period_jobs_user_id",
                table: "period_jobs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_period_run_counters_user_id_period_type_period_start_period~",
                table: "period_run_counters",
                columns: new[] { "user_id", "period_type", "period_start", "period_end" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_prompt_deliveries_prompt_id",
                table: "prompt_deliveries",
                column: "prompt_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_prompt_deliveries_user_id_period_type_period_start_period_e~",
                table: "prompt_deliveries",
                columns: new[] { "user_id", "period_type", "period_start", "period_end", "sent_at" },
                unique: true);
        }
    }
}
