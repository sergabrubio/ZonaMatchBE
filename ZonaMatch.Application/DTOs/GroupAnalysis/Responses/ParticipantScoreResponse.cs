namespace ZonaMatch.Application.DTOs.GroupAnalysis.Responses;

public sealed record ParticipantScoreResponse(
    Guid ParticipantId,
    string Name,
    decimal Weight,
    decimal NormalizedWeight,
    int? Score);
