using System.Text.Json;
using ZonaMatch.Application.DTOs;
using ZonaMatch.Application.DTOs.Analysis.Requests;
using ZonaMatch.Application.DTOs.Analysis.Responses;
using ZonaMatch.Application.DTOs.GroupAnalysis.Responses;
using ZonaMatch.Application.Exceptions;
using ZonaMatch.Application.Interfaces;
using ZonaMatch.Application.UseCases.Comunidad;
using ZonaMatch.Application.UseCases.Perfil;
using ZonaMatch.Domain.Entities;

namespace ZonaMatch.Application.UseCases.AnalisisGuardados
{
    public static class AnalisisMapeo
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public static AnalysisResponse ToResponse(Analisis analisis, IEnumerable<Analisis>? versions = null) =>
            new(
                analisis.Id,
                analisis.Nombre,
                analisis.AnalisisOrigenId,
                analisis.Zonas
                    .OrderBy(z => z.Orden)
                    .Select(z => new AnalysisZoneResponse(z.ZonaSlug, z.Nombre, z.EsInicial, z.EsAgregada))
                    .ToList(),
                CriterioUsuario.ContextoADto(analisis.Contexto),
                CriterioUsuario.CriteriosADto(analisis.Contexto),
                CriterioUsuario.PuntosADto(analisis.Contexto),
                analisis.FechaCreacion,
                analisis.FechaActualizacion,
                analisis.PuntajesEnGrupoJson is null
                    ? null
                    : GroupRankingSnapshotJson.Deserialize(analisis.PuntajesEnGrupoJson, JsonOptions),
                (versions ?? [])
                    .OrderBy(v => v.FechaCreacion)
                    .Select(v => ToResponse(v))
                    .ToList());

        // Zonas del pedido: existentes, sin repetir y con a lo sumo una inicial
        public static async Task<List<AnalisisZona>> ArmarZonasAsync(
            IReadOnlyList<AnalysisZoneRequest> zones, IZonaRepository repository, CancellationToken cancellationToken)
        {
            if (zones.Count == 0)
                throw new DatosInvalidosException("Elegí al menos una zona.");

            if (zones.Select(zone => zone.Slug).Distinct(StringComparer.Ordinal).Count() != zones.Count)
                throw new DatosInvalidosException("Hay zonas repetidas en el análisis.");

            if (zones.Count(zone => zone.IsInitial) > 1)
                throw new DatosInvalidosException("Solo puede haber una zona inicial.");

            foreach (var zone in zones)
                await repository.ValidarExisteAsync(zone.Slug, cancellationToken);

            return zones
                .Select((zone, index) => new AnalisisZona
                {
                    Orden = index + 1,
                    ZonaSlug = zone.Slug,
                    Nombre = zone.Name.Trim(),
                    EsInicial = zone.IsInitial,
                    EsAgregada = zone.IsAdded,
                })
                .ToList();
        }
    }
}
