using System.ComponentModel.DataAnnotations;

namespace ZonaMatch.Application.DTOs.GroupAnalysis.Requests;

public sealed class CalculateGroupScoreRequest
{
    [MaxLength(5, ErrorMessage = "Se pueden agregar hasta 5 zonas después del ranking.")]
    public List<string> AddedZones { get; set; } = [];

    [Required]
    [MinLength(1, ErrorMessage = "Indicá el peso de cada participante.")]
    [MaxLength(5, ErrorMessage = "Un grupo puede tener hasta 5 participantes.")]
    public List<ParticipantWeightRequest> ParticipantWeights { get; set; } = [];

    [Range(typeof(decimal), "0", "999999999999")]
    public decimal? MaximumRent { get; set; }
}
