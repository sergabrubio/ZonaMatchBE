using System.ComponentModel.DataAnnotations;
using ZonaMatch.Application.DTOs.GroupAnalysis.Responses;

namespace ZonaMatch.Application.DTOs
{
    public sealed class CompararAnalisisRequest
    {
        [Required]
        [MinLength(2, ErrorMessage = "Elegí al menos dos análisis para comparar.")]
        [MaxLength(5, ErrorMessage = "Se pueden comparar hasta cinco análisis a la vez.")]
        public List<Guid> AnalisisIds { get; set; } = [];
    }

    public sealed record ComparacionAnalisisDto(IReadOnlyList<AnalisisComparadoDto> Analisis);

    public sealed record AnalisisComparadoDto(
        Guid Id,
        string Nombre,
        string ModoViaje,
        IReadOnlyList<PuntajeZonaDto> Zonas,
        GroupRankingResponse? PuntajesEnGrupo = null);
}
