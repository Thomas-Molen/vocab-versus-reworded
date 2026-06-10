using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wordset.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Extensions required for trigram fuzzy matching and edit-distance fuzzy matching
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS fuzzystrmatch;");

            migrationBuilder.CreateTable(
                name: "wordsets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    share_code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wordsets", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wordsets_share_code",
                table: "wordsets",
                column: "share_code",
                unique: true);

            // Create words as a LIST-partitioned table. EF Core cannot express this natively,
            // so we use raw SQL. Each wordset's partition is provisioned at runtime
            // by WordsetRepository.ProvisionPartitionAsync when a wordset is created.
            migrationBuilder.Sql(
                """
                CREATE TABLE words (
                    wordset_id      UUID    NOT NULL,
                    word            TEXT    NOT NULL,
                    frequency_rank  INTEGER,
                    PRIMARY KEY (wordset_id, word)
                ) PARTITION BY LIST (wordset_id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "wordsets");
            migrationBuilder.Sql("DROP TABLE IF EXISTS words;");
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS fuzzystrmatch;");
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS pg_trgm;");
        }
    }
}
