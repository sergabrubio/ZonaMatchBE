using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using ZonaMatch.Application.Interfaces;
using ZonaMatch.Domain.Puntaje;
using ZonaMatch.Infrastructure.Data;

namespace ZonaMatch.Infrastructure.Repositories
{
    // Arma el perfil de todas las zonas elegidas de geo.zonas para el motor de puntaje: censo y delitos
    // (geo.zona_indicadores), transporte (geo.zona_transporte_*), equipamiento de OSM y alquiler
    // (app.alquileres_zona). Contar el equipamiento de todas las zonas tarda unos segundos, asi que el resultado
    // se guarda en PerfilesZonaCache: los datos solo cambian cuando se vuelve a correr el ETL.
    public class PerfilesZonaRepository : IPerfilesZonaRepository
    {
        private const string Geo = ZonaMatchDbOptions.GeoSchema;
        private const string Osm = ZonaMatchDbOptions.OsmSchema;
        private const string App = ZonaMatchDbOptions.AppSchema;

        // Mismas etiquetas que las categorias de PuntoInteresRepository (educacion, salud, espacios verdes).
        // Los poligonos cuentan por un punto interior; la superficie verde es la de los parques dentro de la zona,
        // en m2 reales (el area en 3857 se corrige por cos^2 de la latitud).
        private static readonly string SqlPerfiles = $"""
            WITH zonas AS (
                SELECT DISTINCT ON (z.slug) z.osm_id, z.slug, z.nombre, z.nivel, z.superficie_km2, z.geom,
                       ST_Transform(z.geom, 3857) AS way
                FROM {Geo}.zonas z
                WHERE z.elegida
                  -- This OSM locality is a duplicate representation of all CABA; other same-area
                  -- zones (for example neighborhoods matching their comuna) are valid ranking zones.
                  AND z.slug <> 'buenos-aires'
                ORDER BY z.slug, z.nivel DESC
            ),
            etiquetados AS (
                SELECT amenity, leisure, way FROM {Osm}.planet_osm_point
                WHERE name IS NOT NULL
                  AND (amenity IN ('kindergarten', 'school', 'college', 'university', 'hospital', 'clinic', 'doctors')
                       OR leisure IN ('park', 'garden', 'nature_reserve'))
                UNION ALL
                SELECT amenity, leisure, ST_PointOnSurface(way) FROM {Osm}.planet_osm_polygon
                WHERE name IS NOT NULL
                  AND (amenity IN ('kindergarten', 'school', 'college', 'university', 'hospital', 'clinic', 'doctors')
                       OR leisure IN ('park', 'garden', 'nature_reserve'))
            ),
            puntos AS (
                SELECT CASE
                           WHEN amenity IN ('kindergarten', 'school', 'college', 'university') THEN 'educacion'
                           WHEN amenity IN ('hospital', 'clinic', 'doctors') THEN 'salud'
                           ELSE 'verde'
                       END AS categoria, way
                FROM etiquetados
            ),
            equipamiento AS (
                SELECT z.osm_id,
                       count(*) FILTER (WHERE p.categoria = 'educacion')::int AS escuelas,
                       count(*) FILTER (WHERE p.categoria = 'salud')::int AS salud,
                       count(*) FILTER (WHERE p.categoria = 'verde')::int AS verdes
                FROM zonas z
                LEFT JOIN puntos p ON ST_Intersects(p.way, z.way)
                GROUP BY z.osm_id
            ),
            verde AS (
                SELECT z.osm_id,
                       COALESCE(sum(ST_Area(ST_Intersection(ST_MakeValid(v.way), z.way))
                                    * cos(radians(ST_Y(ST_Centroid(z.geom)))) ^ 2), 0) AS m2
                FROM zonas z
                LEFT JOIN {Osm}.planet_osm_polygon v
                       ON v.leisure IN ('park', 'garden', 'nature_reserve') AND ST_Intersects(v.way, z.way)
                GROUP BY z.osm_id
            ),
            lineas AS (
                SELECT osm_id, sum(lineas)::int AS lineas FROM {Geo}.zona_transporte_paradas GROUP BY osm_id
            ),
            pico AS (
                SELECT osm_id, sum(salidas)::int AS salidas FROM {Geo}.zona_transporte_lineas
                WHERE franja = 'manana' GROUP BY osm_id
            ),
            -- Un registro por zona base; las comunas y los partidos suman los de las zonas que contienen
            -- (promedio ponderado por propiedades disponibles)
            alquiler AS (
                SELECT z.osm_id,
                       CASE WHEN z.nivel > 5 THEN a.alquiler_promedio ELSE agregado.promedio END AS promedio,
                       CASE WHEN z.nivel > 5 THEN a.propiedades_disponibles ELSE agregado.propiedades END AS propiedades
                FROM zonas z
                LEFT JOIN {App}.alquileres_zona a ON a.zona_slug = z.slug
                LEFT JOIN LATERAL (
                    SELECT round(sum(b.alquiler_promedio * greatest(b.propiedades_disponibles, 1))
                                 / sum(greatest(b.propiedades_disponibles, 1))) AS promedio,
                           sum(b.propiedades_disponibles)::int AS propiedades
                    FROM zonas h
                    JOIN {App}.alquileres_zona b ON b.zona_slug = h.slug
                    WHERE z.nivel <= 5 AND h.nivel > 5 AND ST_Contains(z.geom, ST_PointOnSurface(h.geom))
                ) agregado ON true
            )
            SELECT z.slug AS "Slug", z.nombre AS "Nombre", z.nivel AS "Nivel", z.superficie_km2 AS "SuperficieKm2",
                   COALESCE(i.poblacion, 0) AS "Poblacion", i.delitos_anio AS "Delitos",
                   COALESCE(l.lineas, 0) AS "LineasTransporte", COALESCE(p.salidas, 0) AS "SalidasHoraPico",
                   e.escuelas AS "Escuelas", e.salud AS "CentrosSalud", e.verdes AS "EspaciosVerdes",
                   v.m2 AS "SuperficieVerdeM2",
                   ST_Y({ZonaSql.Centro("z.geom")}) AS "Latitud", ST_X({ZonaSql.Centro("z.geom")}) AS "Longitud",
                   a.promedio AS "AlquilerPromedio", a.propiedades AS "PropiedadesDisponibles"
            FROM zonas z
            LEFT JOIN {Geo}.zona_indicadores i USING (osm_id)
            LEFT JOIN lineas l USING (osm_id)
            LEFT JOIN pico p USING (osm_id)
            JOIN equipamiento e USING (osm_id)
            JOIN verde v USING (osm_id)
            JOIN alquiler a USING (osm_id)
            """;

        private static readonly string SqlZonasDelEntorno = $"""
            WITH zonas_validas AS (
                -- Buenos Aires repeats the full CABA boundary; keep other same-area neighborhoods.
                SELECT z.*
                FROM {Geo}.zonas z
                WHERE z.elegida
                  AND z.slug <> 'buenos-aires'
            ),
            puntos AS (
                SELECT p.orden, ST_SetSRID(ST_MakePoint(p.longitud, p.latitud), 4326) AS geom
                FROM unnest(@latitudes::double precision[], @longitudes::double precision[])
                     WITH ORDINALITY AS p(latitud, longitud, orden)
            ),
            contenedoras AS (
                SELECT p.orden, z.osm_id, z.slug, z.nivel, z.geom
                FROM puntos p
                JOIN zonas_validas z ON ST_Covers(z.geom, p.geom)
            ),
            mas_especificas AS (
                SELECT c.*
                FROM contenedoras c
                WHERE c.nivel = (
                    SELECT max(c2.nivel)
                    FROM contenedoras c2
                    WHERE c2.orden = c.orden
                )
            ),
            segmentos AS (
                SELECT a.orden AS origen, b.orden AS destino,
                       ST_MakeLine(a.geom, b.geom) AS geom,
                       greatest(za.nivel, zb.nivel) AS nivel
                FROM puntos a
                JOIN puntos b ON a.orden < b.orden
                JOIN mas_especificas za ON za.orden = a.orden
                JOIN mas_especificas zb ON zb.orden = b.orden
            )
            SELECT DISTINCT candidatas.slug AS "Value"
            FROM (
                SELECT vecina.slug
                FROM mas_especificas base
                JOIN zonas_validas vecina
                  ON vecina.nivel = base.nivel
                 AND ST_Intersects(vecina.geom, base.geom)

                UNION

                SELECT intermedia.slug
                FROM segmentos tramo
                JOIN zonas_validas intermedia
                  ON intermedia.nivel = tramo.nivel
                 AND ST_Intersects(intermedia.geom, tramo.geom)
            ) candidatas
            """;

        private readonly ZonaMatchDbContext _dbContext;
        private readonly PerfilesZonaCache _cache;
        private readonly ILogger<PerfilesZonaRepository> _logger;

        public PerfilesZonaRepository(ZonaMatchDbContext dbContext, PerfilesZonaCache cache, ILogger<PerfilesZonaRepository> logger)
        {
            _dbContext = dbContext;
            _cache = cache;
            _logger = logger;
        }

        public Task<IReadOnlyList<PerfilZona>> ListarAsync(CancellationToken cancellationToken = default) =>
            _cache.ObtenerAsync(CalcularAsync, cancellationToken);

        public async Task<IReadOnlyList<string>> ListarZonasDelEntornoAsync(
            IReadOnlyCollection<(double Latitud, double Longitud)> ubicaciones,
            CancellationToken cancellationToken = default)
        {
            if (ubicaciones.Count == 0)
                return [];

            var latitudes = ubicaciones.Select(point => point.Latitud).ToArray();
            var longitudes = ubicaciones.Select(point => point.Longitud).ToArray();

            try
            {
                return await _dbContext.Database
                    .SqlQueryRaw<string>(
                        SqlZonasDelEntorno,
                        new NpgsqlParameter("latitudes", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = latitudes },
                        new NpgsqlParameter("longitudes", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = longitudes })
                    .ToListAsync(cancellationToken);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                _logger.LogWarning("Faltan las tablas de zonas ({Mensaje}). Correr DATOS-ZONAS-DOCKER.bat.", ex.MessageText);
                return [];
            }
        }

        private async Task<IReadOnlyList<PerfilZona>> CalcularAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Recorre todo el AMBA: mas tiempo que el comando por defecto
                _dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(2));

                var filas = await _dbContext.Database
                    .SqlQueryRaw<PerfilFila>(SqlPerfiles)
                    .ToListAsync(cancellationToken);

                return filas.Select(f => f.APerfil()).ToList();
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                _logger.LogWarning("Faltan tablas para el puntaje de zonas ({Mensaje}). Correr DATOS-ZONAS-DOCKER.bat.", ex.MessageText);
                return [];
            }
        }

        // Resultado sin clave de SqlPerfiles; los nombres coinciden con los alias de las columnas
        private sealed class PerfilFila
        {
            public string Slug { get; set; } = string.Empty;
            public string Nombre { get; set; } = string.Empty;
            public int Nivel { get; set; }
            public double SuperficieKm2 { get; set; }
            public int Poblacion { get; set; }
            public int? Delitos { get; set; }
            public int LineasTransporte { get; set; }
            public int SalidasHoraPico { get; set; }
            public int Escuelas { get; set; }
            public int CentrosSalud { get; set; }
            public int EspaciosVerdes { get; set; }
            public double SuperficieVerdeM2 { get; set; }
            public double Latitud { get; set; }
            public double Longitud { get; set; }
            public decimal? AlquilerPromedio { get; set; }
            public int? PropiedadesDisponibles { get; set; }

            public PerfilZona APerfil() => new()
            {
                Slug = Slug,
                Nombre = Nombre,
                Nivel = Nivel,
                SuperficieKm2 = SuperficieKm2,
                Poblacion = Poblacion,
                Delitos = Delitos,
                LineasTransporte = LineasTransporte,
                SalidasHoraPico = SalidasHoraPico,
                Escuelas = Escuelas,
                CentrosSalud = CentrosSalud,
                EspaciosVerdes = EspaciosVerdes,
                SuperficieVerdeM2 = SuperficieVerdeM2,
                Latitud = Latitud,
                Longitud = Longitud,
                AlquilerPromedio = AlquilerPromedio,
                PropiedadesDisponibles = PropiedadesDisponibles,
            };
        }
    }
}
