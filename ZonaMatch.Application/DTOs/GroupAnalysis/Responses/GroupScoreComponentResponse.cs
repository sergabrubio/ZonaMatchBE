namespace ZonaMatch.Application.DTOs.GroupAnalysis.Responses;

public sealed record GroupScoreComponentResponse(
    string Criterion,
    int? Score,
    string Details,
    IReadOnlyList<ParticipantScoreComponentResponse> Participants);
