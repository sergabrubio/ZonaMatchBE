using System.ComponentModel.DataAnnotations;
using ZonaMatch.Application.DTOs;

namespace ZonaMatch.Application.DTOs.Analysis.Requests;

public sealed class CreateAnalysisVersionRequest
{
    [StringLength(50, MinimumLength = 1, ErrorMessage = "El nombre debe tener entre 1 y 50 caracteres.")]
    public string? Name { get; set; }

    [Required(ErrorMessage = "El criterio es obligatorio.")]
    public CriterioUsuarioRequest Criterion { get; set; } = null!;

    public List<AnalysisZoneRequest>? Zones { get; set; }
}
