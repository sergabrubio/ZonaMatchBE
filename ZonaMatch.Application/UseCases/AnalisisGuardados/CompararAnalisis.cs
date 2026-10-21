using System.Text.RegularExpressions;
using System.Text.Json;
using ZonaMatch.Application.DTOs;
using ZonaMatch.Application.DTOs.GroupAnalysis.Responses;
using ZonaMatch.Application.Exceptions;
using ZonaMatch.Application.Interfaces;
using ZonaMatch.Application.UseCases.Puntajes;
using ZonaMatch.Domain.Entities;
using ZonaMatch.Domain.Puntaje;

namespace ZonaMatch.Application.UseCases.AnalisisGuardados
{
    /// <summary>
    /// Recalcula las zonas elegidas en cada análisis usando la copia de criterios que quedó guardada con él.
    /// </summary>
    public sealed class CompararAnalisis
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private const int MaximoAnalisis = 5;
        private const int MaximoZonas = 10;
        private const int ViajesEnParalelo = 6;

        private readonly IAnalisisRepository _analisis;
        private readonly IPerfilesZonaRepository _perfilesZona;
        private readonly IRuteoClient _ruteo;

        public CompararAnalisis(
            IAnalisisRepository analisis,
            IPerfilesZonaRepository perfilesZona,
            IRuteoClient ruteo)
        {
            _analisis = analisis;
            _perfilesZona = perfilesZona;
            _ruteo = ruteo;
        }

        public async Task<ComparacionAnalisisDto> EjecutarAsync(
            Guid usuarioId,
            CompararAnalisisRequest solicitud,
            CancellationToken cancellationToken = default)
        {
            if (solicitud.AnalisisIds.Count is < 2 or > MaximoAnalisis)
                throw new DatosInvalidosException("Elegí entre dos y cinco análisis para comparar.");

            if (solicitud.AnalisisIds.Distinct().Count() != solicitud.AnalisisIds.Count)
                throw new DatosInvalidosException("No se puede comparar el mismo análisis más de una vez.");

            var ownedAnalyses = await _analisis.ListarDeUsuarioAsync(usuarioId, cancellationToken);
            var byId = ownedAnalyses.ToDictionary(analysis => analysis.Id);
            if (solicitud.AnalisisIds.Any(id => !byId.ContainsKey(id)))
                throw new AnalisisNoEncontradoException();

            var selectedAnalyses = solicitud.AnalisisIds.Select(id => byId[id]).ToArray();
            var analysesWithoutZones = selectedAnalyses.Where(analysis => analysis.Zonas.Count == 0).ToArray();
            if (analysesWithoutZones.Length > 0)
                throw new DatosInvalidosException("Todos los análisis elegidos deben tener al menos una zona.");

            var analysesWithTooManyZones = selectedAnalyses.Where(analysis => analysis.Zonas.Count > MaximoZonas).ToArray();
            if (analysesWithTooManyZones.Length > 0)
                throw new DatosInvalidosException("Cada análisis puede incluir hasta diez zonas en la comparación.");

            var personalAnalyses = selectedAnalyses
                .Where(analysis => analysis.PuntajesEnGrupoJson is null)
                .ToArray();
            var selectedPersonalZones = personalAnalyses
                .SelectMany(analysis => analysis.Zonas)
                .GroupBy(zone => zone.ZonaSlug, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();

            var zonesBySlug = new Dictionary<string, PerfilZona>(StringComparer.Ordinal);
            MotorPuntaje? motor = null;
            if (personalAnalyses.Length > 0)
            {
                var universe = await _perfilesZona.ListarAsync(cancellationToken);
                zonesBySlug = universe.ToDictionary(zone => zone.Slug, StringComparer.Ordinal);
                var unavailableZones = selectedPersonalZones
                    .Where(zone => !zonesBySlug.ContainsKey(zone.ZonaSlug))
                    .Select(zone => zone.Nombre)
                    .ToArray();
                if (unavailableZones.Length > 0)
                    throw new DatosInvalidosException($"No hay datos para calcular estas zonas: {string.Join(", ", unavailableZones)}.");

                motor = new MotorPuntaje(universe);
            }

            using var routeLimit = new SemaphoreSlim(ViajesEnParalelo);

            var calculations = await Task.WhenAll(selectedAnalyses.Select(async analysis =>
            {
                if (analysis.PuntajesEnGrupoJson is { } groupRankingJson)
                    return FromGroupSnapshot(analysis, groupRankingJson);

                var context = analysis.Contexto;
                var request = BuildRequest(context);
                var mode = ModosViaje.DesdeMovilidad(context.Movilidad ?? []);
                var analysisZones = analysis.Zonas
                    .OrderBy(zone => zone.Orden)
                    .Select(zone => zonesBySlug[zone.ZonaSlug])
                    .ToArray();
                var travelTimes = await MeasureTripsAsync(analysisZones, request.Lugares, mode, routeLimit, cancellationToken);
                var rankedZones = analysisZones
                    .Select(zone => ToDto(motor!.Calcular(zone, request, travelTimes[zone.Slug])))
                    .OrderByDescending(zone => zone.Puntaje.HasValue)
                    .ThenByDescending(zone => zone.Puntaje)
                    .ThenBy(zone => zone.Nombre, StringComparer.CurrentCulture)
                    .ToArray();

                return new AnalisisComparadoDto(
                    analysis.Id,
                    analysis.Nombre,
                    ModosViaje.Codigo(mode),
                    rankedZones,
                    null);
            }));

            if (calculations.All(result => result.Zonas.All(zone => zone.Puntaje is null)))
                throw new DatosInvalidosException("No hay datos suficientes para puntuar las zonas de los análisis elegidos.");

            return new ComparacionAnalisisDto(calculations);
        }

        private static AnalisisComparadoDto FromGroupSnapshot(Analisis analysis, string serializedRanking)
        {
            GroupRankingResponse? snapshot;
            try
            {
                snapshot = GroupRankingSnapshotJson.Deserialize(serializedRanking, JsonOptions);
            }
            catch (JsonException)
            {
                throw new DatosInvalidosException($"El análisis de grupo '{analysis.Nombre}' no tiene un resultado válido guardado.");
            }

            if (snapshot?.Zones is not { } snapshotZones)
                throw new DatosInvalidosException($"El análisis de grupo '{analysis.Nombre}' no tiene un resultado válido guardado.");

            var scoresBySlug = snapshotZones
                .GroupBy(zone => zone.Slug, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var savedZones = analysis.Zonas.OrderBy(zone => zone.Orden).ToArray();
            var missingSlugs = savedZones
                .Where(zone => !scoresBySlug.ContainsKey(zone.ZonaSlug))
                .Select(zone => zone.ZonaSlug)
                .ToArray();
            if (missingSlugs.Length > 0)
                throw new DatosInvalidosException(
                    $"El análisis de grupo '{analysis.Nombre}' no tiene puntajes guardados para todas sus zonas. Volvé a calcular y guardar ese ranking.");

            var rankedGroupZones = savedZones
                .Select(zone => scoresBySlug[zone.ZonaSlug])
                .OrderByDescending(zone => zone.Score.HasValue)
                .ThenByDescending(zone => zone.Score)
                .ThenBy(zone => zone.Name, StringComparer.CurrentCulture)
                .ToArray();
            var zones = rankedGroupZones
                .Select(ToDto)
                .ToArray();

            return new AnalisisComparadoDto(
                analysis.Id,
                analysis.Nombre,
                "grupo",
                zones,
                snapshot with { Zones = rankedGroupZones, NearbyZones = [] });
        }

        private async Task<Dictionary<string, IReadOnlyList<TiempoViaje?>>> MeasureTripsAsync(
            IReadOnlyList<PerfilZona> zones,
            IReadOnlyList<LugarDelUsuario> places,
            ModoViaje mode,
            SemaphoreSlim limiter,
            CancellationToken cancellationToken)
        {
            async Task<TiempoViaje?> MeasureAsync(PerfilZona zone, LugarDelUsuario place)
            {
                if (!place.TieneUbicacion)
                    return null;

                await limiter.WaitAsync(cancellationToken);
                try
                {
                    return await _ruteo.CalcularAsync(
                               zone.Latitud, zone.Longitud,
                               place.Latitud!.Value, place.Longitud!.Value,
                               mode, cancellationToken)
                           ?? ModosViaje.Estimar(
                               mode, zone.Latitud, zone.Longitud,
                               place.Latitud!.Value, place.Longitud!.Value);
                }
                finally
                {
                    limiter.Release();
                }
            }

            var zoneTasks = zones.Select(async zone =>
                (zone.Slug, Times: await Task.WhenAll(places.Select(place => MeasureAsync(zone, place)))));
            var results = await Task.WhenAll(zoneTasks);
            return results.ToDictionary(
                result => result.Slug,
                result => (IReadOnlyList<TiempoViaje?>)result.Times,
                StringComparer.Ordinal);
        }

        private static PedidoPuntaje BuildRequest(Contexto context)
        {
            var criteria = context.Criterios
                .Where(criterion => MotorPuntaje.EsCriterioConocido(criterion.CriterioId)
                                    && criterion.Prioridad is "baja" or "media" or "alta")
                .ToDictionary(criterion => criterion.CriterioId, criterion => criterion.Prioridad, StringComparer.Ordinal);

            var places = context.Puntos
                .OrderBy(point => point.Orden)
                .Select(point => new LugarDelUsuario(
                    point.Nombre,
                    point.Latitude,
                    point.Longitude,
                    point.Prioridad,
                    ParseMinutes(point.TiempoMax)))
                .ToArray();

            return new PedidoPuntaje(criteria, places, ParseRent(context.AlquilerMaximo));
        }

        private static int ParseMinutes(string? value)
        {
            var match = Regex.Match(value ?? string.Empty, @"\d+");
            return match.Success && int.TryParse(match.Value, out var minutes) && minutes > 0
                ? minutes
                : CalcularPuntajes.TiempoMaximoPorDefecto;
        }

        private static decimal? ParseRent(string? value)
        {
            var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
            return decimal.TryParse(digits, out var amount) && amount > 0 ? amount : null;
        }

        private static PuntajeZonaDto ToDto(ResultadoZona result) =>
            new(
                result.Zona.Slug,
                result.Zona.Nombre,
                result.Puntaje,
                result.Componentes
                    .Select(component => new ComponentePuntajeDto(
                        component.Criterio, component.Prioridad, component.Puntaje, component.Detalle))
                    .ToArray(),
                result.Limites
                    .Select(limit => new LimitePuntajeDto(limit.Tipo, limit.Cumple, limit.Detalle))
                    .ToArray(),
                result.Lugares
                    .Select(place => new TiempoLugarDto(place.Nombre, place.Minutos, place.MaximoMinutos, place.Estimado))
                    .ToArray(),
                result.Zona.AlquilerPromedio is { } average
                    ? new AlquilerZonaDto(average, result.Zona.PropiedadesDisponibles ?? 0)
                    : null);

        private static PuntajeZonaDto ToDto(RankedZoneResponse result) =>
            new(
                result.Slug,
                result.Name,
                result.Score,
                result.Components?.Select(component => new ComponentePuntajeDto(
                    component.Criterion,
                    "ponderado",
                    component.Score,
                    component.Details)).ToArray() ?? [],
                result.Limits?.Select(limit => new LimitePuntajeDto(
                    limit.Type,
                    limit.IsMet,
                    limit.Details)).ToArray() ?? [],
                result.Places?.Select(place => new TiempoLugarDto(
                    $"{place.ParticipantName}: {place.Name}",
                    place.Minutes,
                    place.MaximumMinutes,
                    place.IsEstimated)).ToArray() ?? [],
                result.Rent is { } rent
                    ? new AlquilerZonaDto(rent.Average, rent.AvailableProperties)
                    : null);
    }
}
