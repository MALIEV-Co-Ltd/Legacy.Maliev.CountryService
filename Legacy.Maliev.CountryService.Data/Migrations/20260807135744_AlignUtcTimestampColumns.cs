using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Legacy.Maliev.CountryService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlignUtcTimestampColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Npgsql/PostgreSQL otherwise applies the session time zone while converting
            // timestamptz to timestamp. Explicitly interpret the existing instant as UTC
            // so values are preserved when the legacy wall-clock representation is used.
            migrationBuilder.Sql(
                """
                ALTER TABLE "Country" ALTER COLUMN "ModifiedDate" DROP DEFAULT;
                ALTER TABLE "Country" ALTER COLUMN "ModifiedDate"
                    TYPE timestamp without time zone
                    USING "ModifiedDate" AT TIME ZONE 'UTC';
                ALTER TABLE "Country" ALTER COLUMN "ModifiedDate"
                    SET DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'UTC');
                ALTER TABLE "Country" ALTER COLUMN "CreatedDate" DROP DEFAULT;
                ALTER TABLE "Country" ALTER COLUMN "CreatedDate"
                    TYPE timestamp without time zone
                    USING "CreatedDate" AT TIME ZONE 'UTC';
                ALTER TABLE "Country" ALTER COLUMN "CreatedDate"
                    SET DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'UTC');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "Country" ALTER COLUMN "ModifiedDate" DROP DEFAULT;
                ALTER TABLE "Country" ALTER COLUMN "ModifiedDate"
                    TYPE timestamp with time zone
                    USING "ModifiedDate" AT TIME ZONE 'UTC';
                ALTER TABLE "Country" ALTER COLUMN "ModifiedDate"
                    SET DEFAULT CURRENT_TIMESTAMP;
                ALTER TABLE "Country" ALTER COLUMN "CreatedDate" DROP DEFAULT;
                ALTER TABLE "Country" ALTER COLUMN "CreatedDate"
                    TYPE timestamp with time zone
                    USING "CreatedDate" AT TIME ZONE 'UTC';
                ALTER TABLE "Country" ALTER COLUMN "CreatedDate"
                    SET DEFAULT CURRENT_TIMESTAMP;
                """);
        }
    }
}
