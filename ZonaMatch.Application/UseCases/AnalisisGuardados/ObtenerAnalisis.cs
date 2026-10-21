using ZonaMatch.Application.DTOs;
using ZonaMatch.Application.DTOs.Analysis.Responses;
using ZonaMatch.Application.Exceptions;
using ZonaMatch.Application.Interfaces;

namespace ZonaMatch.Application.UseCases.AnalisisGuardados
{
    // Un analisis del usuario; si es un original, con sus versiones
    public class ObtenerAnalisis
    {
        private readonly IAnalisisRepository _analisis;

        public ObtenerAnalisis(IAnalisisRepository analisis)
        {
            _analisis = analisis;
        }

        public async Task<AnalysisResponse> EjecutarAsync(Guid id, Guid usuarioId, CancellationToken cancellationToken = default)
        {
            var analisis = await _analisis.ObtenerAsync(id, usuarioId, cancellationToken)
                ?? throw new AnalisisNoEncontradoException();

            var versiones = analisis.AnalisisOrigenId is null
                ? await _analisis.ListarVersionesAsync(analisis.Id, cancellationToken)
                : [];

            return AnalisisMapeo.ToResponse(analisis, versiones);
        }
    }
}
