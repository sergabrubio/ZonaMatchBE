namespace ZonaMatch.Application.DTOs.GroupAnalysis.Responses;

public sealed record RankedZoneResponse(
    string Slug,
    string Name,
    int? Score,
    IReadOnlyList<ParticipantScoreResponse> Participants,
    IReadOnlyList<GroupScoreComponentResponse>? Components = null,
    IReadOnlyList<ScoreLimitResponse>? Limits = null,
    IReadOnlyList<ParticipantTripResponse>? Places = null,
    ZoneRentResponse? Rent = null);
