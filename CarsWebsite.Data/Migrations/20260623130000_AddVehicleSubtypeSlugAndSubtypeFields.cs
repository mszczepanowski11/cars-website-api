using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace cars_website_api.Migrations
{
    public partial class AddVehicleSubtypeSlugAndSubtypeFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "vehiclesubtypes",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OperatingWeightKg",
                table: "caradverts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkingWidthCm",
                table: "caradverts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxDiggingDepthM",
                table: "caradverts",
                type: "decimal(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BucketCapacityL",
                table: "caradverts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TankCapacityL",
                table: "caradverts",
                type: "int",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Slug", table: "vehiclesubtypes");
            migrationBuilder.DropColumn(name: "OperatingWeightKg", table: "caradverts");
            migrationBuilder.DropColumn(name: "WorkingWidthCm", table: "caradverts");
            migrationBuilder.DropColumn(name: "MaxDiggingDepthM", table: "caradverts");
            migrationBuilder.DropColumn(name: "BucketCapacityL", table: "caradverts");
            migrationBuilder.DropColumn(name: "TankCapacityL", table: "caradverts");
        }
    }
}
