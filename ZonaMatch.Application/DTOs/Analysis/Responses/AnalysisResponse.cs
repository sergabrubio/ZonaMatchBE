using ZonaMatch.Application.DTOs;
using ZonaMatch.Application.DTOs.GroupAnalysis.Responses;

namespace ZonaMatch.Application.DTOs.Analysis.Responses;

public sealed record AnalysisResponse(
    Guid Id,
    string Name,
    Guid? OriginAnalysisId,
    IReadOnlyList<AnalysisZoneResponse> Zones,
    ContextoDto Context,
    Dictionary<string, string> Criteria,
    PuntosDto Places,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    GroupRankingResponse? GroupScores,
    IReadOnlyList<AnalysisResponse> Versions);
