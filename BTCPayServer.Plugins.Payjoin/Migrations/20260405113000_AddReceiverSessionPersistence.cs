using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using System;

namespace BTCPayServer.Plugins.Payjoin.Migrations
{
    [DbContext(typeof(PayjoinPluginDbContext))]
    [Migration("20260405113000_AddReceiverSessionPersistence")]
    public partial class AddReceiverSessionPersistence : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.CreateTable(
                name: "ReceiverSessions",
                schema: "BTCPayServer.Plugins.Payjoin",
                columns: table => new
                {
                    InvoiceId = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "text", nullable: false),
                    ReceiverAddress = table.Column<string>(type: "text", nullable: false),
                    OhttpRelayUrl = table.Column<string>(type: "text", nullable: false),
                    MonitoringExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsCloseRequested = table.Column<bool>(type: "boolean", nullable: false),
                    CloseInvoiceStatus = table.Column<int>(type: "integer", nullable: true),
                    CloseRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ContributedInputTransactionId = table.Column<string>(type: "text", nullable: true),
                    ContributedInputOutputIndex = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiverSessions", x => x.InvoiceId);
                });

            migrationBuilder.CreateTable(
                name: "ReceiverSessionEvents",
                schema: "BTCPayServer.Plugins.Payjoin",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InvoiceId = table.Column<string>(type: "text", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Event = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiverSessionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceiverSessionEvents_ReceiverSessions_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "BTCPayServer.Plugins.Payjoin",
                        principalTable: "ReceiverSessions",
                        principalColumn: "InvoiceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiverSessionEvents_InvoiceId_Sequence",
                schema: "BTCPayServer.Plugins.Payjoin",
                table: "ReceiverSessionEvents",
                columns: new[] { "InvoiceId", "Sequence" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.DropTable(
                name: "ReceiverSessionEvents",
                schema: "BTCPayServer.Plugins.Payjoin");

            migrationBuilder.DropTable(
                name: "ReceiverSessions",
                schema: "BTCPayServer.Plugins.Payjoin");
        }
    }
}
