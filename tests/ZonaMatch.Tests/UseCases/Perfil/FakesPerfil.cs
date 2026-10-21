using ZonaMatch.Application.DTOs;
using ZonaMatch.Application.DTOs.Analysis.Requests;
using ZonaMatch.Application.Interfaces;
using ZonaMatch.Domain.Entities;

namespace ZonaMatch.Tests.UseCases.Perfil
{
    // Repositorios en memoria para el perfil y los analisis guardados. Las entidades se guardan por
    // referencia, como las que tienen seguimiento de cambios.

    // seguridad y salud activos; transporte existe pero esta desactivado
    internal sealed class FakeCriterioRepository : ICriterioRepository
    {
        public Task<IReadOnlyList<Criterio>> ListarAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Criterio>>(
            [
                new Criterio { Id = "seguridad", Nombre = "Seguridad", Activo = true },
                new Criterio { Id = "salud", Nombre = "Salud", Activo = true },
                new Criterio { Id = "transporte", Nombre = "Transporte", Activo = false },
            ]);
    }

    internal sealed class FakePerfilRepository : IPerfilRepository
    {
        public List<Contexto> Perfiles { get; } = [];
        public int Guardados { get; private set; }

        public Task<Contexto?> ObtenerDeUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Perfiles.FirstOrDefault(p => p.UsuarioId == usuarioId));

        public Task<IReadOnlyList<Contexto>> ObtenerDeUsuariosAsync(
            IReadOnlyCollection<Guid> usuarioIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Contexto>>(
                Perfiles.Where(perfil => usuarioIds.Contains(perfil.UsuarioId)).ToList());

        public void Agregar(Contexto perfil) => Perfiles.Add(perfil);

        public Task GuardarCambiosAsync(CancellationToken cancellationToken = default)
        {
            Guardados++;
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeAnalisisRepository : IAnalisisRepository
    {
        public List<Analisis> Analisis { get; } = [];
        public int Guardados { get; private set; }

        public Task<IReadOnlyList<Analisis>> ListarDeUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Analisis>>(
                Analisis.Where(a => a.UsuarioId == usuarioId).OrderByDescending(a => a.FechaActualizacion).ToList());

        public Task<Analisis?> ObtenerAsync(Guid id, Guid usuarioId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Analisis.FirstOrDefault(a => a.Id == id && a.UsuarioId == usuarioId));

        public Task<IReadOnlyList<Analisis>> ListarVersionesAsync(Guid analisisOrigenId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Analisis>>(
                Analisis.Where(a => a.AnalisisOrigenId == analisisOrigenId).OrderBy(a => a.FechaCreacion).ToList());

        public void Agregar(Analisis analisis) => Analisis.Add(analisis);

        public void Eliminar(Analisis analisis) => Analisis.Remove(analisis);

        public Task GuardarCambiosAsync(CancellationToken cancellationToken = default)
        {
            Guardados++;
            return Task.CompletedTask;
        }
    }

    internal static class Pedidos
    {
        public static CriterioUsuarioRequest Criterio(
            Dictionary<string, string?>? criterios = null,
            int personas = 1,
            List<string?>? edades = null,
            List<LugarRequest>? lugares = null,
            string? alquiler = null) => new()
        {
            Contexto = new ContextoRequest
            {
                Personas = personas,
                Edades = edades ?? [],
                Movilidad = ["bici"],
                Mascotas = ["gato"],
            },
            Criterios = criterios ?? new() { ["seguridad"] = "alta" },
            Puntos = new PuntosRequest { AlquilerMaximo = alquiler, Lugares = lugares ?? [] },
        };

        public static AnalysisZoneRequest Zona(string slug = FakeZonaRepository.Zona, bool inicial = false) =>
            new() { Slug = slug, Name = "Villa Luro, CABA", IsInitial = inicial };
    }
}
