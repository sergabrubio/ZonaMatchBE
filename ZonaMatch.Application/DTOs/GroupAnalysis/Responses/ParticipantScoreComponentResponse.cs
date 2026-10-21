namespace ZonaMatch.Application.DTOs.GroupAnalysis.Responses;

public sealed record ParticipantScoreComponentResponse(
    Guid ParticipantId,
    string Name,
    decimal NormalizedWeight,
    string Priority,
    int? Score,
    string Details);
