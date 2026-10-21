namespace ZonaMatch.Application.DTOs.GroupAnalysis.Responses;

public sealed record ExcludedParticipantResponse(
    Guid ParticipantId,
    string Name,
    string Reason);
