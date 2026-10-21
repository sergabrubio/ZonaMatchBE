using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZonaMatch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PersistirZonasAgregadasEnAnalisis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "es_agregada",
                schema: "app",
                table: "analisis_zonas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Before this flag existed, extra zones were stored after the automatic top five.
            migrationBuilder.Sql("UPDATE app.analisis_zonas SET es_agregada = TRUE WHERE orden > 5;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "es_agregada",
                schema: "app",
                table: "analisis_zonas");
        }
    }
}
