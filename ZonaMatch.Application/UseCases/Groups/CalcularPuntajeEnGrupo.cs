using System.Text.RegularExpressions;
using ZonaMatch.Application.DTOs;
using ZonaMatch.Application.DTOs.GroupAnalysis.Requests;
using ZonaMatch.Application.DTOs.GroupAnalysis.Responses;
using ZonaMatch.Application.Exceptions;
using ZonaMatch.Application.Interfaces;
using ZonaMatch.Domain.Entities;
using ZonaMatch.Domain.Puntaje;

namespace ZonaMatch.Application.UseCases.Groups;

/// <summary>
/// Scores zones containing group members' places and their contiguous neighbors, then combines the results using the configured weights.
/// </summary>
public sealed partial class CalcularPuntajeEnGrupo
{
    private const int ViajesEnParalelo = 6;
    private const int RecomendadasDevueltas = 4;
    private const int ZonasTopAutomatico = 5;

    private readonly IGroupRepository _groups;
    private readonly IPerfilRepository _profiles;
    private readonly IPerfilesZonaRepository _zoneProfiles;
    private readonly IRuteoClient _routing;

    public CalcularPuntajeEnGrupo(
        IGroupRepository groups,
        IPerfilRepository profiles,
        IPerfilesZonaRepository zoneProfiles,
        IRuteoClient routing)
    {
        _groups = groups;
        _profiles = profiles;
        _zoneProfiles = zoneProfiles;
        _routing = routing;
    }

    public async Task<GroupRankingResponse> EjecutarAsync(
        Guid groupId,
        Guid currentUserId,
        CalculateGroupScoreRequest request,
        CancellationToken cancellationToken = default)
    {
        var group = await _groups.GetDetailsByIdAsync(groupId, cancellationToken)
            ?? throw new NoEncontradoException("No se encontró el grupo.");

        if (!group.Participants.Any(participant => participant.UserId == currentUserId))
            throw new AccesoDenegadoException("No pertenecés a este grupo.");

        var participantIds = group.Participants.Select(participant => participant.Id).ToHashSet();
        var weights = ValidateWeights(request.ParticipantWeights, participantIds);
        var slugs = (request.AddedZones ?? [])
            .Select(slug => slug?.Trim() ?? string.Empty)
            .Where(slug => slug.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var universe = await _zoneProfiles.ListarAsync(cancellationToken);
        var zonesBySlug = universe.ToDictionary(zone => zone.Slug, StringComparer.Ordinal);
        var addedZones = slugs.Where(zonesBySlug.ContainsKey).Select(slug => zonesBySlug[slug]).ToArray();
        if (addedZones.Length != slugs.Length)
            throw new NoEncontradoException("No se encontraron una o más zonas agregadas al análisis.");

        var profiles = await _profiles.ObtenerDeUsuariosAsync(
            group.Participants.Select(participant => participant.UserId).Distinct().ToArray(),
            cancellationToken);
        var profilesByUserId = profiles.ToDictionary(profile => profile.UsuarioId);
        var motor = new MotorPuntaje(universe);
        using var routeLimit = new SemaphoreSlim(ViajesEnParalelo);

        var members = group.Participants
            .Where(participant => profilesByUserId.ContainsKey(participant.UserId))
            .Select(participant =>
            {
                var profile = profilesByUserId[participant.UserId];
                return new MemberInput(
                    participant,
                    BuildRequest(profile, request.MaximumRent),
                    ModosViaje.DesdeMovilidad(profile.Movilidad ?? []),
                    profile.Puntos.OrderBy(point => point.Orden).ToArray());
            })
            .ToArray();

        var locations = members
            .Where(member => weights[member.Participant.Id] > 0)
            .SelectMany(member => member.Request.Lugares)
            .Where(place => place.TieneUbicacion)
            .Select(place => (Latitud: place.Latitud!.Value, Longitud: place.Longitud!.Value))
            .ToArray();
        var nearbySlugs = locations.Length == 0
            ? universe.Select(zone => zone.Slug).ToArray()
            : await _zoneProfiles.ListarZonasDelEntornoAsync(locations, cancellationToken);
        var nearbyZones = nearbySlugs
            .Where(zonesBySlug.ContainsKey)
            .Select(slug => zonesBySlug[slug])
            .ToArray();
        if (locations.Length > 0 && nearbyZones.Length == 0)
            throw new DatosInvalidosException("No se encontraron zonas del catálogo que contengan los puntos de interés de los participantes.");

        var zonesToCalculate = nearbyZones
            .Concat(addedZones)
            .DistinctBy(zone => zone.Slug, StringComparer.Ordinal)
            .ToArray();
        var calculations = members
            .Select(async member =>
            {
                var travelTimes = await MeasureTripsAsync(zonesToCalculate, member.Request.Lugares, member.Mode, routeLimit, cancellationToken);
                var results = zonesToCalculate.ToDictionary(
                    zone => zone.Slug,
                    zone =>
                    {
                        var times = travelTimes[zone.Slug];
                        var result = motor.Calcular(zone, member.Request, times);
                        return new MemberZoneResult(result, BuildPlaceDetails(member, times));
                    },
                    StringComparer.Ordinal);

                return new MemberCalculation(member.Participant, results);
            })
            .ToArray();

        var memberScores = await Task.WhenAll(calculations);
        var hasAnyRanking = zonesToCalculate.Any(zone => memberScores.Any(member =>
            weights[member.Participant.Id] > 0 && member.Zones[zone.Slug].Resultado.Puntaje is not null));
        if (!hasAnyRanking)
            throw new DatosInvalidosException("Ningún participante con peso positivo tiene datos suficientes para puntuar las zonas disponibles.");

        var rankedCandidates = zonesToCalculate
            .Select(zone => BuildZoneResult(zone, group.Participants, weights, memberScores))
            .OrderByDescending(zone => zone.Score.HasValue)
            .ThenByDescending(zone => zone.Score)
            .ThenBy(zone => zone.Name, StringComparer.CurrentCulture)
            .ToArray();

        var rankedZones = rankedCandidates
            .Take(ZonasTopAutomatico)
            .Concat(addedZones.Select(zone => rankedCandidates.Single(result => result.Slug == zone.Slug)))
            .DistinctBy(zone => zone.Slug, StringComparer.Ordinal)
            .OrderByDescending(zone => zone.Score.HasValue)
            .ThenByDescending(zone => zone.Score)
            .ThenBy(zone => zone.Name, StringComparer.CurrentCulture)
            .ToArray();
        var zonesForExclusion = rankedZones.Select(zone => zonesBySlug[zone.Slug]).ToArray();
        var excluded = group.Participants
            .Select(participant => Exclusion(participant, weights[participant.Id], profilesByUserId, zonesForExclusion, memberScores))
            .Where(exclusion => exclusion is not null)
            .Select(exclusion => exclusion!)
            .ToArray();

        var includedSlugs = rankedZones.Select(zone => zone.Slug).ToHashSet(StringComparer.Ordinal);
        var nearby = rankedCandidates
            .Where(zone => !includedSlugs.Contains(zone.Slug) && zone.Score is not null)
            .Take(RecomendadasDevueltas)
            .ToArray();

        var groupPoints = group.Participants
            .Where(participant => profilesByUserId.ContainsKey(participant.UserId))
            .SelectMany(participant => profilesByUserId[participant.UserId].Puntos
                .OrderBy(point => point.Orden)
                .Select(point => new GroupPointOfInterestResponse(
                    point.Id,
                    participant.Id,
                    participant.User.Email,
                    point.Nombre,
                    point.Direccion,
                    point.Latitude,
                    point.Longitude,
                    point.Tipo,
                    point.Prioridad,
                    point.TiempoMax)))
            .ToArray();

        return new GroupRankingResponse(rankedZones, excluded, groupPoints, request.MaximumRent, nearby);
    }

    private static IReadOnlyDictionary<Guid, decimal> ValidateWeights(
        IReadOnlyList<ParticipantWeightRequest> requestedWeights,
        IReadOnlySet<Guid> groupParticipantIds)
    {
        if (requestedWeights.Count != groupParticipantIds.Count)
            throw new DatosInvalidosException("Indicá un peso para cada participante del grupo.");

        var weights = new Dictionary<Guid, decimal>();
        foreach (var entry in requestedWeights)
        {
            if (!groupParticipantIds.Contains(entry.ParticipantId))
                throw new DatosInvalidosException("El pedido incluye a alguien que no pertenece al grupo.");
            if (entry.Weight is < 0 or > 100)
                throw new DatosInvalidosException("Cada peso debe estar entre 0 y 100.");
            if (!weights.TryAdd(entry.ParticipantId, entry.Weight))
                throw new DatosInvalidosException("Hay participantes repetidos en la configuración de pesos.");
        }

        if (weights.Keys.Count != groupParticipantIds.Count || weights.Values.All(weight => weight == 0))
            throw new DatosInvalidosException("Asigná un peso mayor a cero a por lo menos un participante.");

        return weights;
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
                return await _routing.CalcularAsync(
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
        return results.ToDictionary(result => result.Slug, result => (IReadOnlyList<TiempoViaje?>)result.Times, StringComparer.Ordinal);
    }

    private static PedidoPuntaje BuildRequest(Contexto profile, decimal? sharedRentLimit)
    {
        var criteria = profile.Criterios
            .Where(criterion => MotorPuntaje.EsCriterioConocido(criterion.CriterioId)
                                && criterion.Prioridad is "baja" or "media" or "alta")
            .ToDictionary(criterion => criterion.CriterioId, criterion => criterion.Prioridad, StringComparer.Ordinal);

        var places = profile.Puntos
            .OrderBy(point => point.Orden)
            .Select(point => new LugarDelUsuario(
                point.Nombre,
                point.Latitude,
                point.Longitude,
                point.Prioridad,
                ParseMinutes(point.TiempoMax)))
            .ToArray();

        return new PedidoPuntaje(criteria, places, sharedRentLimit is > 0 ? sharedRentLimit : null);
    }

    private static int ParseMinutes(string? value)
    {
        var match = MinutesRegex().Match(value ?? string.Empty);
        return match.Success && int.TryParse(match.Value, out var minutes) && minutes > 0
            ? minutes
            : 45;
    }

    private static IReadOnlyList<ParticipantTripResponse> BuildPlaceDetails(
        MemberInput member,
        IReadOnlyList<TiempoViaje?> times) => member.Points
        .Select((point, index) =>
        {
            var maximumMinutes = ParseMinutes(point.TiempoMax);
            var time = index < times.Count ? times[index] : null;
            return new ParticipantTripResponse(
                point.Id,
                member.Participant.Id,
                member.Participant.User.Email,
                point.Nombre,
                point.Direccion,
                point.Tipo,
                point.Prioridad,
                ModosViaje.Codigo(member.Mode),
                time?.Minutos,
                maximumMinutes,
                time?.Estimado ?? false,
                time is null ? null : MotorPuntaje.PuntajeViaje(time.Minutos, maximumMinutes));
        })
        .ToArray();

    private static RankedZoneResponse BuildZoneResult(
        PerfilZona zone,
        IEnumerable<Participant> participants,
        IReadOnlyDictionary<Guid, decimal> weights,
        IReadOnlyList<MemberCalculation> memberScores)
    {
        var scoresByParticipant = memberScores.ToDictionary(member => member.Participant.Id);
        var available = participants
            .Select(participant =>
            {
                var score = scoresByParticipant.TryGetValue(participant.Id, out var calculation)
                    && calculation.Zones.TryGetValue(zone.Slug, out var zoneResult)
                    ? zoneResult.Resultado.Puntaje
                    : null;
                return (Participant: participant, Score: score, Weight: weights[participant.Id]);
            })
            .ToArray();
        var scored = available.Where(entry => entry.Score is not null && entry.Weight > 0).ToArray();
        var totalWeight = scored.Sum(entry => entry.Weight);
        int? groupScore = totalWeight > 0
            ? (int)Math.Clamp(decimal.Round(
                scored.Sum(entry => entry.Score!.Value * entry.Weight) / totalWeight,
                0, MidpointRounding.AwayFromZero), 0, 100)
            : null;

        var breakdown = available
            .Select(entry => new ParticipantScoreResponse(
                entry.Participant.Id,
                entry.Participant.User.Email,
                entry.Weight,
                entry.Score is not null && entry.Weight > 0 && totalWeight > 0
                    ? decimal.Round(entry.Weight / totalWeight * 100, 2, MidpointRounding.AwayFromZero)
                    : 0,
                entry.Score))
            .ToArray();
        var normalizedWeights = breakdown.ToDictionary(item => item.ParticipantId, item => item.NormalizedWeight);

        var componentGroups = memberScores
            .Where(member => member.Zones.ContainsKey(zone.Slug))
            .SelectMany(member => member.Zones[zone.Slug].Resultado.Componentes
                .Select(component => (Member: member, Component: component)))
            .GroupBy(entry => entry.Component.Criterio, StringComparer.Ordinal)
            .Select(group =>
            {
                var withScore = group
                    .Where(entry => entry.Component.Puntaje is not null && weights[entry.Member.Participant.Id] > 0)
                    .ToArray();
                var componentWeight = withScore.Sum(entry => weights[entry.Member.Participant.Id]);
                int? componentScore = componentWeight > 0
                    ? (int)Math.Clamp(decimal.Round(
                        withScore.Sum(entry => entry.Component.Puntaje!.Value * weights[entry.Member.Participant.Id]) / componentWeight,
                        0, MidpointRounding.AwayFromZero), 0, 100)
                    : null;
                var componentParticipants = group
                    .Select(entry => new ParticipantScoreComponentResponse(
                        entry.Member.Participant.Id,
                        entry.Member.Participant.User.Email,
                        entry.Component.Puntaje is not null && weights[entry.Member.Participant.Id] > 0 && componentWeight > 0
                            ? decimal.Round(weights[entry.Member.Participant.Id] / componentWeight * 100, 2, MidpointRounding.AwayFromZero)
                            : 0,
                        entry.Component.Prioridad,
                        entry.Component.Puntaje,
                        entry.Component.Detalle))
                    .ToArray();
                var details = group.Select(entry => entry.Component.Detalle).Where(detail => !string.IsNullOrWhiteSpace(detail)).Distinct().ToArray();
                var detail = group.Key == MotorPuntaje.Lugares
                    ? "Promedio ponderado de los viajes individuales con datos disponibles."
                    : string.Join(" · ", details);

                return new GroupScoreComponentResponse(group.Key, componentScore, detail, componentParticipants);
            })
            .OrderBy(component => component.Criterion, StringComparer.Ordinal)
            .ToArray();

        var participantLimits = memberScores
            .Where(member => member.Zones.ContainsKey(zone.Slug))
            .SelectMany(member => member.Zones[zone.Slug].Resultado.Limites.Select(limit =>
                new ScoreLimitResponse(
                    member.Participant.Id,
                    member.Participant.User.Email,
                    normalizedWeights.GetValueOrDefault(member.Participant.Id),
                    limit.Tipo,
                    limit.Cumple,
                    limit.Detalle)))
            .ToArray();
        var limits = participantLimits
            .Where(limit => limit.Type == MotorPuntaje.LimiteAlquiler)
            .Take(1)
            .Select(limit => limit with { ParticipantId = Guid.Empty, ParticipantName = "Grupo", NormalizedWeight = 100 })
            .Concat(participantLimits.Where(limit => limit.Type != MotorPuntaje.LimiteAlquiler))
            .ToArray();
        var places = memberScores
            .Where(member => member.Zones.ContainsKey(zone.Slug))
            .SelectMany(member => member.Zones[zone.Slug].Places)
            .ToArray();
        var rent = zone.AlquilerPromedio is { } average
            ? new ZoneRentResponse(average, zone.PropiedadesDisponibles ?? 0)
            : null;

        return new RankedZoneResponse(zone.Slug, zone.Nombre, groupScore, breakdown, componentGroups, limits, places, rent);
    }

    private static ExcludedParticipantResponse? Exclusion(
        Participant participant,
        decimal weight,
        IReadOnlyDictionary<Guid, Contexto> profilesByUserId,
        IReadOnlyList<PerfilZona> selectedZones,
        IReadOnlyList<MemberCalculation> memberScores)
    {
        string? reason = null;
        if (!profilesByUserId.ContainsKey(participant.UserId))
            reason = "No tiene un perfil guardado.";
        else if (weight == 0)
            reason = "Tiene un peso asignado de 0.";
        else if (!memberScores.Any(member => member.Participant.Id == participant.Id && selectedZones.Any(zone =>
                     member.Zones.TryGetValue(zone.Slug, out var result) && result.Resultado.Puntaje is not null)))
            reason = "No obtuvo puntajes con los datos disponibles en las zonas del ranking.";

        return reason is null
            ? null
            : new ExcludedParticipantResponse(participant.Id, participant.User.Email, reason);
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex MinutesRegex();

    private sealed record MemberInput(
        Participant Participant,
        PedidoPuntaje Request,
        ModoViaje Mode,
        IReadOnlyList<PuntoDeInteres> Points);

    private sealed record MemberCalculation(
        Participant Participant,
        IReadOnlyDictionary<string, MemberZoneResult> Zones);

    private sealed record MemberZoneResult(
        ResultadoZona Resultado,
        IReadOnlyList<ParticipantTripResponse> Places);
}
