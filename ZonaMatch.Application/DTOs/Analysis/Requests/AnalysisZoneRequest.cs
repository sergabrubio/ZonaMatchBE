using System.ComponentModel.DataAnnotations;

namespace ZonaMatch.Application.DTOs.Analysis.Requests;

public sealed class AnalysisZoneRequest
{
    [Required(ErrorMessage = "El slug de la zona es obligatorio.")]
    [StringLength(100)]
    public string Slug { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre de la zona es obligatorio.")]
    [StringLength(150)]
    public string Name { get; set; } = string.Empty;

    public bool IsInitial { get; set; }

    public bool IsAdded { get; set; }
}
