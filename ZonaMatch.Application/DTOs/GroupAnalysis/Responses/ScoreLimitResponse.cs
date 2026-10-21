namespace ZonaMatch.Application.DTOs.GroupAnalysis.Responses;

public sealed record ScoreLimitResponse(
    Guid ParticipantId,
    string ParticipantName,
    decimal NormalizedWeight,
    string Type,
    bool? IsMet,
    string Details);
