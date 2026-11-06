using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clients_Name",
                table: "Clients");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "TimeEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Tasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "TaskComments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "TaskCommentMentions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Projects",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ProjectMembers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Notifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Labels",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Clients",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ActivityEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsDemo = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantMemberships",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantMemberships", x => new { x.TenantId, x.UserId });
                    table.ForeignKey(
                        name: "FK_TenantMemberships_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TenantMemberships_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantSupportAccesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSupportAccesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantSupportAccesses_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Data: the default tenant holds everything that existed before multitenancy (ADR 0012, 0014).
            // Ids must match TenantDefaults.DefaultId.
            migrationBuilder.Sql(@"
INSERT INTO [Tenants] ([Id], [Name], [Slug], [IsDemo], [CreatedAt])
VALUES ('0192f6a0-0000-7000-8000-000000000001', N'TaskFlow', N'default', 0, SYSDATETIMEOFFSET())");

            migrationBuilder.Sql("UPDATE [TimeEntries] SET [TenantId] = '0192f6a0-0000-7000-8000-000000000001'");
            migrationBuilder.Sql("UPDATE [Tasks] SET [TenantId] = '0192f6a0-0000-7000-8000-000000000001'");
            migrationBuilder.Sql("UPDATE [TaskComments] SET [TenantId] = '0192f6a0-0000-7000-8000-000000000001'");
            migrationBuilder.Sql("UPDATE [TaskCommentMentions] SET [TenantId] = '0192f6a0-0000-7000-8000-000000000001'");
            migrationBuilder.Sql("UPDATE [Projects] SET [TenantId] = '0192f6a0-0000-7000-8000-000000000001'");
            migrationBuilder.Sql("UPDATE [ProjectMembers] SET [TenantId] = '0192f6a0-0000-7000-8000-000000000001'");
            migrationBuilder.Sql("UPDATE [Notifications] SET [TenantId] = '0192f6a0-0000-7000-8000-000000000001'");
            migrationBuilder.Sql("UPDATE [Labels] SET [TenantId] = '0192f6a0-0000-7000-8000-000000000001'");
            migrationBuilder.Sql("UPDATE [Clients] SET [TenantId] = '0192f6a0-0000-7000-8000-000000000001'");
            migrationBuilder.Sql("UPDATE [ActivityEntries] SET [TenantId] = '0192f6a0-0000-7000-8000-000000000001'");

            // Memberships: Identity Admin -> Owner, everyone else -> Member. Demo accounts (Demo:Accounts defaults
            // @taskflow.demo and the seeded teammates @demo.taskflow.invalid) are left out and lose the Admin role.
            migrationBuilder.Sql(@"
DECLARE @demo TABLE ([Id] uniqueidentifier PRIMARY KEY);
INSERT INTO @demo SELECT [Id] FROM [AspNetUsers]
WHERE [NormalizedEmail] LIKE N'%@TASKFLOW.DEMO' OR [NormalizedEmail] LIKE N'%@DEMO.TASKFLOW.INVALID';

DELETE ur FROM [AspNetUserRoles] ur
JOIN [AspNetRoles] r ON r.[Id] = ur.[RoleId]
WHERE r.[NormalizedName] = N'ADMIN' AND ur.[UserId] IN (SELECT [Id] FROM @demo);

INSERT INTO [TenantMemberships] ([TenantId], [UserId], [Role], [JoinedAt])
SELECT '0192f6a0-0000-7000-8000-000000000001', u.[Id],
  CASE WHEN EXISTS (SELECT 1 FROM [AspNetUserRoles] ur JOIN [AspNetRoles] r ON r.[Id] = ur.[RoleId]
                    WHERE ur.[UserId] = u.[Id] AND r.[NormalizedName] = N'ADMIN') THEN 'Owner' ELSE 'Member' END,
  SYSDATETIMEOFFSET()
FROM [AspNetUsers] u
WHERE u.[Id] NOT IN (SELECT [Id] FROM @demo)");

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "TimeEntries",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Tasks",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "TaskComments",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "TaskCommentMentions",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Projects",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "ProjectMembers",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Notifications",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Labels",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "Clients",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TenantId",
                table: "ActivityEntries",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_TenantId",
                table: "TimeEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TenantId",
                table: "Tasks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskComments_TenantId",
                table: "TaskComments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskCommentMentions_TenantId",
                table: "TaskCommentMentions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_TenantId",
                table: "Projects",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectMembers_TenantId",
                table: "ProjectMembers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId",
                table: "Notifications",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Labels_TenantId",
                table: "Labels",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_TenantId",
                table: "Clients",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_TenantId_Name",
                table: "Clients",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEntries_TenantId",
                table: "ActivityEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantMemberships_UserId",
                table: "TenantMemberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantSupportAccesses_TenantId_At",
                table: "TenantSupportAccesses",
                columns: new[] { "TenantId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantMemberships");

            migrationBuilder.DropTable(
                name: "TenantSupportAccesses");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_TenantId",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_TenantId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_TaskComments_TenantId",
                table: "TaskComments");

            migrationBuilder.DropIndex(
                name: "IX_TaskCommentMentions_TenantId",
                table: "TaskCommentMentions");

            migrationBuilder.DropIndex(
                name: "IX_Projects_TenantId",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_ProjectMembers_TenantId",
                table: "ProjectMembers");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_TenantId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Labels_TenantId",
                table: "Labels");

            migrationBuilder.DropIndex(
                name: "IX_Clients_TenantId",
                table: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_Clients_TenantId_Name",
                table: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_ActivityEntries_TenantId",
                table: "ActivityEntries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TaskComments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TaskCommentMentions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ProjectMembers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Labels");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ActivityEntries");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_Name",
                table: "Clients",
                column: "Name",
                unique: true);
        }
    }
}
