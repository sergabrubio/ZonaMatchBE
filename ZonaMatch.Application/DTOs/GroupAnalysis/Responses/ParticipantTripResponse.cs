namespace ZonaMatch.Application.DTOs.GroupAnalysis.Responses;

public sealed record ParticipantTripResponse(
    Guid Id,
    Guid ParticipantId,
    string ParticipantName,
    string Name,
    string? Address,
    string? Type,
    string? Priority,
    string TravelMode,
    int? Minutes,
    int MaximumMinutes,
    bool IsEstimated,
    int? Score);
