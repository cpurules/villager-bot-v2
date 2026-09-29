using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VillagerBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "queue_position");

            migrationBuilder.CreateTable(
                name: "active_requests",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    villager_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    queue_position = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('queue_position')"),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false),
                    pulled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    hunter_id = table.Column<long>(type: "bigint", nullable: true),
                    channel_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_active_requests", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "archived_requests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    villager_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pulled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    hunter_id = table.Column<long>(type: "bigint", nullable: true),
                    outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_archived_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bot_state",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bot_state", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "overflow_categories",
                columns: table => new
                {
                    category_id = table.Column<long>(type: "bigint", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_overflow_categories", x => x.category_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_active_requests_channel_id",
                table: "active_requests",
                column: "channel_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_active_requests_pulled_at_is_available_queue_position",
                table: "active_requests",
                columns: new[] { "pulled_at", "is_available", "queue_position" });

            migrationBuilder.CreateIndex(
                name: "ix_active_requests_queue_position",
                table: "active_requests",
                column: "queue_position",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_active_requests_villager_key",
                table: "active_requests",
                column: "villager_key");

            migrationBuilder.CreateIndex(
                name: "ix_archived_requests_outcome_closed_at",
                table: "archived_requests",
                columns: new[] { "outcome", "closed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_archived_requests_user_id_closed_at",
                table: "archived_requests",
                columns: new[] { "user_id", "closed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_archived_requests_villager_key",
                table: "archived_requests",
                column: "villager_key");

            migrationBuilder.CreateIndex(
                name: "ix_overflow_categories_number",
                table: "overflow_categories",
                column: "number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "active_requests");

            migrationBuilder.DropTable(
                name: "archived_requests");

            migrationBuilder.DropTable(
                name: "bot_state");

            migrationBuilder.DropTable(
                name: "overflow_categories");

            migrationBuilder.DropSequence(
                name: "queue_position");
        }
    }
}
