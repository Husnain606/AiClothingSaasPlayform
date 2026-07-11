using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FashionSaaS.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AuditLogs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                EntityName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                NewValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: false),
                UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuditLogs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Roles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<int>(type: "int", nullable: false),
                Scope = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Roles", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "SubscriptionPlans",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PlanType = table.Column<int>(type: "int", nullable: false),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                DurationDays = table.Column<int>(type: "int", nullable: false),
                TrialDays = table.Column<int>(type: "int", nullable: false),
                ProductLimit = table.Column<int>(type: "int", nullable: false),
                UserLimit = table.Column<int>(type: "int", nullable: false),
                AiUsageLimit = table.Column<int>(type: "int", nullable: false),
                StorageLimitMb = table.Column<long>(type: "bigint", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SubscriptionPlans", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Tenants",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Slug = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                LogoUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                CoverImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Tenants", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "UserLoginAttempts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: false),
                UserAgent = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                IsSuccess = table.Column<bool>(type: "bit", nullable: false),
                FailureReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserLoginAttempts", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "BankAccounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                AccountTitleEncrypted = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                AccountNumberEncrypted = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                BankNameEncrypted = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                BranchCodeEncrypted = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                IbanEncrypted = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BankAccounts", x => x.Id);
                table.ForeignKey(
                    name: "FK_BankAccounts_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "TenantSubscriptions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TenantSubscriptions", x => x.Id);
                table.ForeignKey(
                    name: "FK_TenantSubscriptions_SubscriptionPlans_PlanId",
                    column: x => x.PlanId,
                    principalTable: "SubscriptionPlans",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_TenantSubscriptions_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                IsEmailVerified = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Users", x => x.Id);
                table.ForeignKey(
                    name: "FK_Users_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "SubscriptionPayments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                Status = table.Column<int>(type: "int", nullable: false),
                ConfirmedByAdminId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SubscriptionPayments", x => x.Id);
                table.ForeignKey(
                    name: "FK_SubscriptionPayments_TenantSubscriptions_SubscriptionId",
                    column: x => x.SubscriptionId,
                    principalTable: "TenantSubscriptions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SubscriptionPayments_Tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "Tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PasswordHistories",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PasswordHistories", x => x.Id);
                table.ForeignKey(
                    name: "FK_PasswordHistories_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PasswordResetTokens",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TokenHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                IsUsed = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PasswordResetTokens", x => x.Id);
                table.ForeignKey(
                    name: "FK_PasswordResetTokens_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "RefreshTokens",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TokenHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                IsRevoked = table.Column<bool>(type: "bit", nullable: false),
                RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                table.ForeignKey(
                    name: "FK_RefreshTokens_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "UserMfaSettings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                TotpSecretEncrypted = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                IsEnrolled = table.Column<bool>(type: "bit", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserMfaSettings", x => x.Id);
                table.ForeignKey(
                    name: "FK_UserMfaSettings_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "UserRoles",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                table.ForeignKey(
                    name: "FK_UserRoles_Roles_RoleId",
                    column: x => x.RoleId,
                    principalTable: "Roles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_UserRoles_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MfaBackupCodes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserMfaSettingsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CodeHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                IsUsed = table.Column<bool>(type: "bit", nullable: false),
                UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MfaBackupCodes", x => x.Id);
                table.ForeignKey(
                    name: "FK_MfaBackupCodes_UserMfaSettings_UserMfaSettingsId",
                    column: x => x.UserMfaSettingsId,
                    principalTable: "UserMfaSettings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.InsertData(
            table: "Roles",
            columns: new[] { "Id", "CreatedAt", "Name", "Scope", "UpdatedAt" },
            values: new object[,]
            {
                { new Guid("10000000-0000-0000-0000-000000000001"), new DateTime(2026, 6, 23, 12, 5, 2, 981, DateTimeKind.Utc).AddTicks(4429), 1, 1, new DateTime(2026, 6, 23, 12, 5, 2, 981, DateTimeKind.Utc).AddTicks(4576) },
                { new Guid("10000000-0000-0000-0000-000000000002"), new DateTime(2026, 6, 23, 12, 5, 2, 981, DateTimeKind.Utc).AddTicks(4723), 2, 2, new DateTime(2026, 6, 23, 12, 5, 2, 981, DateTimeKind.Utc).AddTicks(4724) },
                { new Guid("10000000-0000-0000-0000-000000000003"), new DateTime(2026, 6, 23, 12, 5, 2, 981, DateTimeKind.Utc).AddTicks(4726), 3, 2, new DateTime(2026, 6, 23, 12, 5, 2, 981, DateTimeKind.Utc).AddTicks(4726) },
                { new Guid("10000000-0000-0000-0000-000000000004"), new DateTime(2026, 6, 23, 12, 5, 2, 981, DateTimeKind.Utc).AddTicks(4728), 4, 2, new DateTime(2026, 6, 23, 12, 5, 2, 981, DateTimeKind.Utc).AddTicks(4729) },
                { new Guid("10000000-0000-0000-0000-000000000005"), new DateTime(2026, 6, 23, 12, 5, 2, 981, DateTimeKind.Utc).AddTicks(4731), 5, 2, new DateTime(2026, 6, 23, 12, 5, 2, 981, DateTimeKind.Utc).AddTicks(4731) },
                { new Guid("10000000-0000-0000-0000-000000000006"), new DateTime(2026, 6, 23, 12, 5, 2, 981, DateTimeKind.Utc).AddTicks(4733), 6, 2, new DateTime(2026, 6, 23, 12, 5, 2, 981, DateTimeKind.Utc).AddTicks(4734) },
                { new Guid("10000000-0000-0000-0000-000000000007"), new DateTime(2026, 6, 23, 12, 5, 2, 981, DateTimeKind.Utc).AddTicks(4736), 7, 3, new DateTime(2026, 6, 23, 12, 5, 2, 981, DateTimeKind.Utc).AddTicks(4736) }
            });

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogs_CreatedAt",
            table: "AuditLogs",
            column: "CreatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogs_EntityName_EntityId",
            table: "AuditLogs",
            columns: new[] { "EntityName", "EntityId" });

        migrationBuilder.CreateIndex(
            name: "IX_BankAccounts_TenantId",
            table: "BankAccounts",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_MfaBackupCodes_UserMfaSettingsId",
            table: "MfaBackupCodes",
            column: "UserMfaSettingsId");

        migrationBuilder.CreateIndex(
            name: "IX_PasswordHistories_UserId",
            table: "PasswordHistories",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_PasswordResetTokens_TokenHash",
            table: "PasswordResetTokens",
            column: "TokenHash");

        migrationBuilder.CreateIndex(
            name: "IX_PasswordResetTokens_UserId",
            table: "PasswordResetTokens",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_RefreshTokens_UserId_IsRevoked",
            table: "RefreshTokens",
            columns: new[] { "UserId", "IsRevoked" });

        migrationBuilder.CreateIndex(
            name: "IX_Roles_Name",
            table: "Roles",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_SubscriptionPayments_SubscriptionId",
            table: "SubscriptionPayments",
            column: "SubscriptionId");

        migrationBuilder.CreateIndex(
            name: "IX_SubscriptionPayments_TenantId",
            table: "SubscriptionPayments",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_Tenants_Email",
            table: "Tenants",
            column: "Email",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Tenants_Slug",
            table: "Tenants",
            column: "Slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TenantSubscriptions_PlanId",
            table: "TenantSubscriptions",
            column: "PlanId");

        migrationBuilder.CreateIndex(
            name: "IX_TenantSubscriptions_TenantId",
            table: "TenantSubscriptions",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_UserLoginAttempts_Email_CreatedAt",
            table: "UserLoginAttempts",
            columns: new[] { "Email", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_UserMfaSettings_UserId",
            table: "UserMfaSettings",
            column: "UserId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_UserRoles_RoleId",
            table: "UserRoles",
            column: "RoleId");

        migrationBuilder.CreateIndex(
            name: "IX_Users_Email",
            table: "Users",
            column: "Email",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Users_TenantId",
            table: "Users",
            column: "TenantId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AuditLogs");

        migrationBuilder.DropTable(
            name: "BankAccounts");

        migrationBuilder.DropTable(
            name: "MfaBackupCodes");

        migrationBuilder.DropTable(
            name: "PasswordHistories");

        migrationBuilder.DropTable(
            name: "PasswordResetTokens");

        migrationBuilder.DropTable(
            name: "RefreshTokens");

        migrationBuilder.DropTable(
            name: "SubscriptionPayments");

        migrationBuilder.DropTable(
            name: "UserLoginAttempts");

        migrationBuilder.DropTable(
            name: "UserRoles");

        migrationBuilder.DropTable(
            name: "UserMfaSettings");

        migrationBuilder.DropTable(
            name: "TenantSubscriptions");

        migrationBuilder.DropTable(
            name: "Roles");

        migrationBuilder.DropTable(
            name: "Users");

        migrationBuilder.DropTable(
            name: "SubscriptionPlans");

        migrationBuilder.DropTable(
            name: "Tenants");
    }
}
