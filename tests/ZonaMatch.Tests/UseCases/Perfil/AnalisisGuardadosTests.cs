using ZonaMatch.Application.DTOs;
using ZonaMatch.Application.DTOs.Analysis.Requests;
using ZonaMatch.Application.Exceptions;
using ZonaMatch.Application.UseCases.AnalisisGuardados;
using ZonaMatch.Application.UseCases.Perfil;
using ZonaMatch.Domain.Entities;

namespace ZonaMatch.Tests.UseCases.Perfil
{
    public class AnalisisGuardadosTests
    {
        private static readonly DateTimeOffset Ahora = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        private static readonly Guid Usuario = Guid.NewGuid();

        private readonly FakePerfilRepository _perfiles = new();
        private readonly FakeAnalisisRepository _analisis = new();
        private readonly FakeCriterioRepository _criterios = new();
        private readonly FakeZonaRepository _zonas = new();
        private readonly FixedTimeProvider _reloj = new(Ahora);

        private CrearAnalisis Crear => new(_analisis, _perfiles, _zonas, _reloj);
        private CrearVersionAnalisis Versionar => new(_analisis, _criterios, _zonas, _reloj);

        private async Task ConPerfil(string criterio = "seguridad", string prioridad = "alta") =>
            await new GuardarMiPerfil(_perfiles, _criterios, _reloj)
                .EjecutarAsync(Usuario, Pedidos.Criterio(new() { [criterio] = prioridad }));

        private static CreateAnalysisRequest Pedido(string name = "Cerca del trabajo", params AnalysisZoneRequest[] zones) =>
            new() { Name = name, Zones = zones.Length > 0 ? [.. zones] : [Pedidos.Zona(inicial: true)] };

        [Fact]
        public async Task Crear_SinPerfil_LanzaPerfilNoEncontrado()
        {
            await Assert.ThrowsAsync<PerfilNoEncontradoException>(() => Crear.EjecutarAsync(Usuario, Pedido()));
            Assert.Empty(_analisis.Analisis);
        }

        [Fact]
        public async Task Crear_GuardaUnaCopiaDelPerfilQueNoCambiaSiDespuesSeEditaElPerfil()
        {
            await ConPerfil("seguridad", "alta");

            var dto = await Crear.EjecutarAsync(Usuario, Pedido());

            var analisis = Assert.Single(_analisis.Analisis);
            var perfil = Assert.Single(_perfiles.Perfiles);
            Assert.NotSame(perfil, analisis.Contexto);
            Assert.Equal(TipoContexto.Analisis, analisis.Contexto.Tipo);
            Assert.Null(analisis.AnalisisOrigenId);
            Assert.Equal("alta", dto.Criteria["seguridad"]);
            Assert.True(Assert.Single(dto.Zones).IsInitial);

            await ConPerfil("salud", "baja");

            Assert.Equal("seguridad", Assert.Single(analisis.Contexto.Criterios).CriterioId);
        }

        [Fact]
        public async Task Crear_ZonaQueNoExiste_LanzaNoEncontrado()
        {
            await ConPerfil();

            await Assert.ThrowsAsync<NoEncontradoException>(
                () => Crear.EjecutarAsync(Usuario, Pedido(zones: Pedidos.Zona("no-existe"))));
            Assert.Empty(_analisis.Analisis);
        }

        [Fact]
        public async Task Crear_ZonasRepetidas_LanzaError()
        {
            await ConPerfil();

            var error = await Assert.ThrowsAsync<DatosInvalidosException>(
                () => Crear.EjecutarAsync(Usuario, Pedido(zones: [Pedidos.Zona(), Pedidos.Zona()])));
            Assert.Contains("repetidas", error.Message);
        }

        [Fact]
        public async Task Version_CuelgaDelOriginalConElCriterioAjustadoYNombreNumerado()
        {
            await ConPerfil("seguridad", "alta");
            var original = await Crear.EjecutarAsync(Usuario, Pedido("Cerca del trabajo"));

            var version = await Versionar.EjecutarAsync(original.Id, Usuario, new CreateAnalysisVersionRequest
            {
                Criterion = Pedidos.Criterio(new() { ["salud"] = "alta" }),
            });

            Assert.Equal(original.Id, version.OriginAnalysisId);
            Assert.Equal("Cerca del trabajo (2)", version.Name);
            Assert.Equal("alta", version.Criteria["salud"]);
            // Sin zonas en el pedido, compara las mismas que el original
            Assert.Equal(FakeZonaRepository.Zona, Assert.Single(version.Zones).Slug);
            // El original no cambia
            var guardado = _analisis.Analisis.Single(a => a.Id == original.Id);
            Assert.Equal("seguridad", Assert.Single(guardado.Contexto.Criterios).CriterioId);
        }

        [Fact]
        public async Task Version_DeUnaVersion_TambienCuelgaDelOriginal()
        {
            await ConPerfil();
            var original = await Crear.EjecutarAsync(Usuario, Pedido("Facu"));
            var primera = await Versionar.EjecutarAsync(original.Id, Usuario, new() { Criterion = Pedidos.Criterio() });

            var segunda = await Versionar.EjecutarAsync(primera.Id, Usuario, new() { Criterion = Pedidos.Criterio() });

            Assert.Equal(original.Id, segunda.OriginAnalysisId);
            Assert.Equal("Facu (3)", segunda.Name);
        }

        [Fact]
        public async Task Version_DeOtroUsuario_LanzaNoEncontrado()
        {
            await ConPerfil();
            var original = await Crear.EjecutarAsync(Usuario, Pedido());

            await Assert.ThrowsAsync<AnalisisNoEncontradoException>(
                () => Versionar.EjecutarAsync(original.Id, Guid.NewGuid(), new() { Criterion = Pedidos.Criterio() }));
        }

        [Fact]
        public async Task Listar_DevuelveLosOriginalesConSusVersionesAdentro()
        {
            await ConPerfil();
            var original = await Crear.EjecutarAsync(Usuario, Pedido("A"));
            await Crear.EjecutarAsync(Usuario, Pedido("B"));
            await Versionar.EjecutarAsync(original.Id, Usuario, new() { Criterion = Pedidos.Criterio() });

            var listado = await new ListarMisAnalisis(_analisis).EjecutarAsync(Usuario);

            Assert.Equal(2, listado.Count);
            var a = listado.Single(x => x.Name == "A");
            Assert.Equal("A (2)", Assert.Single(a.Versions).Name);
            Assert.Empty(listado.Single(x => x.Name == "B").Versions);
        }

        [Fact]
        public async Task Eliminar_UnOriginal_BorraTambienSusVersiones()
        {
            await ConPerfil();
            var original = await Crear.EjecutarAsync(Usuario, Pedido());
            await Versionar.EjecutarAsync(original.Id, Usuario, new() { Criterion = Pedidos.Criterio() });
            var otro = await Crear.EjecutarAsync(Usuario, Pedido("Otro"));

            await new EliminarAnalisis(_analisis).EjecutarAsync(original.Id, Usuario);

            Assert.Equal(otro.Id, Assert.Single(_analisis.Analisis).Id);
        }

        [Fact]
        public async Task Renombrar_SoloCambiaElNombre()
        {
            await ConPerfil();
            var original = await Crear.EjecutarAsync(Usuario, Pedido("Viejo"));

            var dto = await new RenombrarAnalisis(_analisis, _reloj)
                .EjecutarAsync(original.Id, Usuario, new RenameAnalysisRequest { Name = "  Nuevo " });

            Assert.Equal("Nuevo", dto.Name);
            Assert.Equal(original.Criteria, dto.Criteria);
        }

        [Fact]
        public void NombreDeVersion_RecortaParaNoPasarElMaximo()
        {
            var nombre = CrearVersionAnalisis.NombreDeVersion(new string('a', 50), 12);

            Assert.Equal(50, nombre.Length);
            Assert.EndsWith(" (12)", nombre);
        }
    }
}
