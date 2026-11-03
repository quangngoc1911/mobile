using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowerShop.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BranchScopedIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CommandReceipts_ActorId_Operation_Key",
                table: "CommandReceipts");

            migrationBuilder.CreateIndex(
                name: "IX_CommandReceipts_BranchId_Operation_Key",
                table: "CommandReceipts",
                columns: new[] { "BranchId", "Operation", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CommandReceipts_BranchId_Operation_Key",
                table: "CommandReceipts");

            migrationBuilder.CreateIndex(
                name: "IX_CommandReceipts_ActorId_Operation_Key",
                table: "CommandReceipts",
                columns: new[] { "ActorId", "Operation", "Key" },
                unique: true);
        }
    }
}
