using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExternalLogins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:account_status", "active,disabled,pending,suspended")
                .Annotation("Npgsql:Enum:audit_action", "company_created,email_verified,external_linked,external_unlinked,invitation_withdrawn,login_failure,login_success,member_invited,member_joined,member_removed,register,tenant_switch,tenant_switch_denied,token_refresh,token_revoke")
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:Enum:account_status", "active,disabled,pending,suspended")
                .OldAnnotation("Npgsql:Enum:audit_action", "company_created,email_verified,invitation_withdrawn,login_failure,login_success,member_invited,member_joined,member_removed,register,tenant_switch,tenant_switch_denied,token_refresh,token_revoke")
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "external_logins",
                columns: table => new
                {
                    provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_logins", x => new { x.provider, x.provider_subject });
                    table.ForeignKey(
                        name: "FK_external_logins_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_external_logins_user_id_provider",
                table: "external_logins",
                columns: new[] { "user_id", "provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_logins");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:account_status", "active,disabled,pending,suspended")
                .Annotation("Npgsql:Enum:audit_action", "company_created,email_verified,invitation_withdrawn,login_failure,login_success,member_invited,member_joined,member_removed,register,tenant_switch,tenant_switch_denied,token_refresh,token_revoke")
                .Annotation("Npgsql:PostgresExtension:citext", ",,")
                .OldAnnotation("Npgsql:Enum:account_status", "active,disabled,pending,suspended")
                .OldAnnotation("Npgsql:Enum:audit_action", "company_created,email_verified,external_linked,external_unlinked,invitation_withdrawn,login_failure,login_success,member_invited,member_joined,member_removed,register,tenant_switch,tenant_switch_denied,token_refresh,token_revoke")
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,");
        }
    }
}
