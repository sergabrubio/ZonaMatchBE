using System.ComponentModel.DataAnnotations;

namespace ZonaMatch.Application.DTOs.Analysis.Requests;

public sealed class CreateAnalysisRequest
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "El nombre debe tener entre 1 y 50 caracteres.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Las zonas son obligatorias.")]
    [MinLength(1, ErrorMessage = "Elegí al menos una zona.")]
    public List<AnalysisZoneRequest> Zones { get; set; } = [];
}
