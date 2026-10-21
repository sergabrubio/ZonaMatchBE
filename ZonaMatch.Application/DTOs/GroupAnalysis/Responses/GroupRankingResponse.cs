namespace ZonaMatch.Application.DTOs.GroupAnalysis.Responses;

public sealed record GroupRankingResponse(
    IReadOnlyList<RankedZoneResponse> Zones,
    IReadOnlyList<ExcludedParticipantResponse> ExcludedParticipants,
    IReadOnlyList<GroupPointOfInterestResponse> ParticipantPoints,
    decimal? MaximumRent = null,
    IReadOnlyList<RankedZoneResponse>? NearbyZones = null);
