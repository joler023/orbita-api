using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContactTrigramSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE EXTENSION IF NOT EXISTS pg_trgm;

                CREATE INDEX ix_contacts_display_name_trgm
                    ON contacts USING gin (display_name gin_trgm_ops);
                CREATE INDEX ix_contacts_phone_trgm
                    ON contacts USING gin (phone gin_trgm_ops);
                CREATE INDEX ix_contacts_instagram_trgm
                    ON contacts USING gin (instagram_username gin_trgm_ops);
                CREATE INDEX ix_contacts_email_trgm
                    ON contacts USING gin (email gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS ix_contacts_email_trgm;
                DROP INDEX IF EXISTS ix_contacts_instagram_trgm;
                DROP INDEX IF EXISTS ix_contacts_phone_trgm;
                DROP INDEX IF EXISTS ix_contacts_display_name_trgm;
                """);
        }
    }
}
