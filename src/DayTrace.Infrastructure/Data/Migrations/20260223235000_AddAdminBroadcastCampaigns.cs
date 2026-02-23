using System;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace DayTrace.Infrastructure.Data.Migrations
{
    [DbContext(typeof(DayTraceDbContext))]
    [Migration("20260223235000_AddAdminBroadcastCampaigns")]
    public partial class AddAdminBroadcastCampaigns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_broadcast_campaigns",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    created_by_admin_user_id = table.Column<long>(type: "bigint", nullable: false),
                    audience = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    text = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "queued"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    queued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_broadcast_campaigns", x => x.id);
                    table.ForeignKey(
                        name: "FK_admin_broadcast_campaigns_admin_users_created_by_admin_user_id",
                        column: x => x.created_by_admin_user_id,
                        principalTable: "admin_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_admin_broadcast_campaigns_audience_created_at",
                table: "admin_broadcast_campaigns",
                columns: new[] { "audience", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_admin_broadcast_campaigns_created_at",
                table: "admin_broadcast_campaigns",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_admin_broadcast_campaigns_created_by_admin_user_id",
                table: "admin_broadcast_campaigns",
                column: "created_by_admin_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_admin_broadcast_campaigns_status",
                table: "admin_broadcast_campaigns",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_broadcast_campaigns");
        }
    }
}
