using System.ComponentModel.DataAnnotations;
using ZonaMatch.Application.DTOs.Analysis.Requests;

namespace ZonaMatch.Application.DTOs.GroupAnalysis.Requests;

public sealed class SaveGroupAnalysisRequest
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "El nombre debe tener entre 1 y 50 caracteres.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Las zonas son obligatorias.")]
    [MinLength(1, ErrorMessage = "Elegí al menos una zona.")]
    [MaxLength(10, ErrorMessage = "Se pueden comparar hasta 10 zonas.")]
    public List<AnalysisZoneRequest> Zones { get; set; } = [];

    [MaxLength(5, ErrorMessage = "Se pueden agregar hasta 5 zonas después del ranking.")]
    public List<string> AddedZones { get; set; } = [];

    [Required]
    [MinLength(1, ErrorMessage = "Indicá el peso de cada participante.")]
    [MaxLength(5, ErrorMessage = "Un grupo puede tener hasta 5 participantes.")]
    public List<ParticipantWeightRequest> ParticipantWeights { get; set; } = [];

    [Range(typeof(decimal), "0", "999999999999")]
    public decimal? MaximumRent { get; set; }
}
