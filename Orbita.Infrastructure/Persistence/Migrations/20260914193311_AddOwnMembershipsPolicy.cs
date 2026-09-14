using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orbita.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ORB-A16. A second Row Level Security policy on <c>memberships</c>, so a person can
    /// find out which organizations they belong to.
    ///
    /// <para><b>The problem it solves.</b> The access token carries no tenant claim and the
    /// tenant is a route parameter everywhere else, so right after signing in there is
    /// nothing that says which organization to open — and on a device that has never been
    /// used, nothing remembered to fall back on. The obvious query ("my memberships") could
    /// not be written: <c>tenant_isolation</c> matches on <c>app.tenant_id</c>, so with no
    /// tenant set it returns zero rows, silently.</para>
    ///
    /// <para><b>Why this is not a hole.</b> Postgres combines permissive policies with OR,
    /// so this widens what a session can see — but only a session that set
    /// <c>app.user_id</c>, and only to rows that are that person's own. Three things keep
    /// it tight:</para>
    /// <list type="number">
    /// <item><c>set_config(..., is_local := true)</c>: the value dies with its transaction,
    /// so the policy is inert on every path that does not deliberately set it, and a pooled
    /// connection cannot carry it into whatever runs next.</item>
    /// <item>Exactly one place in the application sets it —
    /// <c>UnitOfWork.QueryInUserScopeAsync</c> — and it takes the id from the authenticated
    /// session's <c>sub</c> claim, never from a request.</item>
    /// <item>It matches on <c>user_id</c>, so it exposes the caller's own row in each
    /// tenant and not the other members of those tenants.</item>
    /// </list>
    ///
    /// <para>The alternative considered and rejected was a second, non-RLS'd table holding
    /// the same facts (the shape <c>invitation_tokens</c> and <c>subscriptions</c> use to
    /// be readable before a tenant is known). It would duplicate membership state, and a
    /// copy that drifts out of sync is a worse failure than this policy is a risk.</para>
    /// </summary>
    public partial class AddOwnMembershipsPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No FOR clause, so it covers SELECT as well as writes — but with no WITH CHECK
            // it permits reads only: an INSERT or UPDATE still has to satisfy
            // tenant_isolation's WITH CHECK, so nobody can write themselves into a tenant
            // this way. Reading is the entire intent.
            migrationBuilder.Sql(
                """
                CREATE POLICY own_memberships ON memberships
                    FOR SELECT
                    USING (user_id = NULLIF(current_setting('app.user_id', true), '')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => migrationBuilder.Sql("DROP POLICY IF EXISTS own_memberships ON memberships;");
    }
}
