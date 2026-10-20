using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cashier.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyProduct",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyProduct", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyProduct_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DailyProduct_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyProduct_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyProduct_CreatedAt",
                table: "DailyProduct",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DailyProduct_CustomerId",
                table: "DailyProduct",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyProduct_ProductId",
                table: "DailyProduct",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyProduct_UserId",
                table: "DailyProduct",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyProduct");
        }
    }
}
