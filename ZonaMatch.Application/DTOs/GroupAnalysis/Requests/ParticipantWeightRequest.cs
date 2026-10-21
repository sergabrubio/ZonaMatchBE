using System.ComponentModel.DataAnnotations;

namespace ZonaMatch.Application.DTOs.GroupAnalysis.Requests;

public sealed class ParticipantWeightRequest
{
    [Required]
    public Guid ParticipantId { get; set; }

    [Range(typeof(decimal), "0", "100")]
    public decimal Weight { get; set; }
}
