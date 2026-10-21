using ZonaMatch.Application.DTOs;
using ZonaMatch.Application.DTOs.Analysis.Responses;
using ZonaMatch.Application.Interfaces;

namespace ZonaMatch.Application.UseCases.AnalisisGuardados
{
    // Listado de analisis del usuario: cada original con sus versiones adentro, el mas reciente primero.
    // Un original se ordena por su ultima actividad, contando la de sus versiones.
    public class ListarMisAnalisis
    {
        private readonly IAnalisisRepository _analisis;

        public ListarMisAnalisis(IAnalisisRepository analisis)
        {
            _analisis = analisis;
        }

        public async Task<IReadOnlyList<AnalysisResponse>> EjecutarAsync(Guid usuarioId, CancellationToken cancellationToken = default)
        {
            var todos = await _analisis.ListarDeUsuarioAsync(usuarioId, cancellationToken);
            var versionesPorOrigen = todos
                .Where(a => a.AnalisisOrigenId is not null)
                .ToLookup(a => a.AnalisisOrigenId!.Value);

            return todos
                .Where(a => a.AnalisisOrigenId is null)
                .Select(original => new
                {
                    Original = original,
                    Versiones = versionesPorOrigen[original.Id].ToList(),
                })
                .OrderByDescending(x => x.Versiones
                    .Select(v => v.FechaActualizacion)
                    .Append(x.Original.FechaActualizacion)
                    .Max())
                .Select(x => AnalisisMapeo.ToResponse(x.Original, x.Versiones))
                .ToList();
        }
    }
}
