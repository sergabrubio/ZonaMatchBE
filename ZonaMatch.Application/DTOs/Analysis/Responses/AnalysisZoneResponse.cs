namespace ZonaMatch.Application.DTOs.Analysis.Responses;

public sealed record AnalysisZoneResponse(
    string Slug,
    string Name,
    bool IsInitial,
    bool IsAdded);
