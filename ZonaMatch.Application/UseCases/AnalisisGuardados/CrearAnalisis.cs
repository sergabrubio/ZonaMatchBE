using ZonaMatch.Application.DTOs;
using ZonaMatch.Application.DTOs.Analysis.Requests;
using ZonaMatch.Application.DTOs.Analysis.Responses;
using ZonaMatch.Application.Exceptions;
using ZonaMatch.Application.Interfaces;
using ZonaMatch.Domain.Entities;

namespace ZonaMatch.Application.UseCases.AnalisisGuardados
{
    // Guarda un analisis original: las zonas que el usuario eligio comparar y una copia de su perfil,
    // que es el criterio con el que se calcula. Sin perfil no hay analisis (PerfilNoEncontradoException).
    public class CrearAnalisis
    {
        private readonly IAnalisisRepository _analisis;
        private readonly IPerfilRepository _perfiles;
        private readonly IZonaRepository _zonas;
        private readonly TimeProvider _reloj;

        public CrearAnalisis(IAnalisisRepository analisis, IPerfilRepository perfiles, IZonaRepository zonas, TimeProvider reloj)
        {
            _analisis = analisis;
            _perfiles = perfiles;
            _zonas = zonas;
            _reloj = reloj;
        }

        public async Task<AnalysisResponse> EjecutarAsync(
            Guid usuarioId, CreateAnalysisRequest solicitud, CancellationToken cancellationToken = default)
        {
            var perfil = await _perfiles.ObtenerDeUsuarioAsync(usuarioId, cancellationToken)
                ?? throw new PerfilNoEncontradoException();

            var zonas = await AnalisisMapeo.ArmarZonasAsync(solicitud.Zones, _zonas, cancellationToken);
            var ahora = _reloj.GetUtcNow();

            var analisis = new Analisis
            {
                UsuarioId = usuarioId,
                Nombre = solicitud.Name.Trim(),
                Contexto = perfil.CopiarParaAnalisis(ahora),
                FechaCreacion = ahora,
                FechaActualizacion = ahora,
                Zonas = zonas,
            };

            _analisis.Agregar(analisis);
            await _analisis.GuardarCambiosAsync(cancellationToken);

            return AnalisisMapeo.ToResponse(analisis);
        }
    }
}
