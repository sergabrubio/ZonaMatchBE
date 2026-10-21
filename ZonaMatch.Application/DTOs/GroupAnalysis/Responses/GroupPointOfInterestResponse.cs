namespace ZonaMatch.Application.DTOs.GroupAnalysis.Responses;

public sealed record GroupPointOfInterestResponse(
    Guid Id,
    Guid ParticipantId,
    string ParticipantName,
    string Name,
    string? Address,
    double? Latitude,
    double? Longitude,
    string? Type,
    string? Priority,
    string? MaximumTime);
