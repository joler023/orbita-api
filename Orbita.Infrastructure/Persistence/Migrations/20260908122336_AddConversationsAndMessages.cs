using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Bundles what the story plan described as two migrations
    /// (AddConversationsAndMessages + ExtendContactsForChannels) into one — there is no
    /// dependency between them and no real database has applied either yet, so keeping
    /// them separate would only add ceremony. See CLAUDE.md's inbound-processing section.
    /// </remarks>
    public partial class AddConversationsAndMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ig_user_id",
                table: "contacts",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_seen_at",
                table: "contacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ai_agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    window_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    human_agent_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    unread_count = table.Column<int>(type: "integer", nullable: false),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_message_preview = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    first_response_seconds = table.Column<int>(type: "integer", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversations", x => x.id);
                    table.ForeignKey(
                        name: "FK_conversations_channel_accounts_channel_account_id",
                        column: x => x.channel_account_id,
                        principalTable: "channel_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_conversations_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_conversations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // messages is hand-written rather than a normal migrationBuilder.CreateTable:
            // Postgres requires a partitioned table's primary key to include the
            // partition column, which is exactly the (id, created_at) composite Message
            // already uses (see MessageConfiguration's remarks). Converting an
            // already-large messages table to partitioned later is one of the most
            // expensive reprocesses there is; doing it now, empty, is a few lines.
            migrationBuilder.Sql(
                """
                CREATE TABLE messages (
                    id uuid NOT NULL,
                    created_at timestamptz NOT NULL,
                    tenant_id uuid NOT NULL,
                    conversation_id uuid NOT NULL,
                    direction character varying(10) NOT NULL,
                    category character varying(20) NOT NULL,
                    body text NULL,
                    media_key text NULL,
                    media_mime character varying(120) NULL,
                    external_id character varying(200) NULL,
                    reply_to_external_id character varying(200) NULL,
                    template_id uuid NULL,
                    sent_by_user_id uuid NULL,
                    ai_run_id uuid NULL,
                    status character varying(20) NOT NULL,
                    error_code character varying(60) NULL,
                    sent_at timestamptz NULL,
                    delivered_at timestamptz NULL,
                    read_at timestamptz NULL,
                    CONSTRAINT "PK_messages" PRIMARY KEY (id, created_at),
                    CONSTRAINT "FK_messages_conversations_conversation_id" FOREIGN KEY (conversation_id)
                        REFERENCES conversations (id) ON DELETE CASCADE,
                    CONSTRAINT "FK_messages_tenants_tenant_id" FOREIGN KEY (tenant_id)
                        REFERENCES tenants (id) ON DELETE CASCADE
                ) PARTITION BY RANGE (created_at);

                -- A UNIQUE index on a partitioned table must include the partition key,
                -- so this can't be a true global "one row per external_id" constraint —
                -- IMessageRepository.FindByExternalIdAsync (checked before every insert)
                -- plus a single consumer per channel account is what actually keeps
                -- idempotency global. This index is the cheap backstop within a partition.
                CREATE UNIQUE INDEX ux_messages_external ON messages (external_id, created_at)
                    WHERE external_id IS NOT NULL;

                -- RLS on a partitioned table is inherited by every partition automatically
                -- when queried through the parent, so this one policy covers all of them.
                ALTER TABLE messages ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON messages
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                -- orbita_app has no DDL privileges, so a background job can never create
                -- partitions itself — maintaining this range (past 2028-12) is a future
                -- migration's job, not runtime code's.
                DO $$
                DECLARE
                    range_start date := DATE '2026-09-01';
                    partition_start date;
                    partition_end date;
                BEGIN
                    FOR i IN 0..27 LOOP
                        partition_start := range_start + (i || ' months')::interval;
                        partition_end := range_start + ((i + 1) || ' months')::interval;
                        EXECUTE format(
                            'CREATE TABLE IF NOT EXISTS messages_%s PARTITION OF messages FOR VALUES FROM (%L) TO (%L)',
                            to_char(partition_start, 'YYYY_MM'),
                            partition_start,
                            partition_end
                        );
                    END LOOP;
                END $$;
                """);

            migrationBuilder.Sql(
                """
                ALTER TABLE conversations ENABLE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation ON conversations
                    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                """);

            // ORB-B03 canonizes Contact.Phone to digits-only (no leading '+') to match
            // Meta's wa_id — fixes up any row created before this change under the old
            // NormalizePhone behavior.
            migrationBuilder.Sql("UPDATE contacts SET phone = ltrim(phone, '+') WHERE phone LIKE '+%';");

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_ig_user_id",
                table: "contacts",
                columns: new[] { "tenant_id", "ig_user_id" },
                unique: true,
                filter: "ig_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_conv_inbox",
                table: "conversations",
                columns: new[] { "tenant_id", "status", "last_message_at" });

            migrationBuilder.CreateIndex(
                name: "IX_conversations_channel_account_id",
                table: "conversations",
                column: "channel_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_contact_id",
                table: "conversations",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_tenant_id_assignee_id_status",
                table: "conversations",
                columns: new[] { "tenant_id", "assignee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_billing",
                table: "messages",
                columns: new[] { "tenant_id", "category", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_messages_conversation_id",
                table: "messages",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_thread",
                table: "messages",
                columns: new[] { "tenant_id", "conversation_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "conversations");

            migrationBuilder.DropIndex(
                name: "ix_contacts_tenant_ig_user_id",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "ig_user_id",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "last_seen_at",
                table: "contacts");
        }
    }
}
