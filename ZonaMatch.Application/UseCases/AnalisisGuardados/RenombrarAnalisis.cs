using ZonaMatch.Application.DTOs;
using ZonaMatch.Application.DTOs.Analysis.Requests;
using ZonaMatch.Application.DTOs.Analysis.Responses;
using ZonaMatch.Application.Exceptions;
using ZonaMatch.Application.Interfaces;

namespace ZonaMatch.Application.UseCases.AnalisisGuardados
{
    // Cambia el nombre de un analisis o de una version. El criterio no se edita: se ajusta con una version nueva.
    public class RenombrarAnalisis
    {
        private readonly IAnalisisRepository _analisis;
        private readonly TimeProvider _reloj;

        public RenombrarAnalisis(IAnalisisRepository analisis, TimeProvider reloj)
        {
            _analisis = analisis;
            _reloj = reloj;
        }

        public async Task<AnalysisResponse> EjecutarAsync(
            Guid id, Guid usuarioId, RenameAnalysisRequest solicitud, CancellationToken cancellationToken = default)
        {
            var analisis = await _analisis.ObtenerAsync(id, usuarioId, cancellationToken)
                ?? throw new AnalisisNoEncontradoException();

            analisis.Nombre = solicitud.Name.Trim();
            analisis.FechaActualizacion = _reloj.GetUtcNow();
            await _analisis.GuardarCambiosAsync(cancellationToken);

            var versiones = analisis.AnalisisOrigenId is null
                ? await _analisis.ListarVersionesAsync(analisis.Id, cancellationToken)
                : [];

            return AnalisisMapeo.ToResponse(analisis, versiones);
        }
    }
}
