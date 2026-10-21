using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ZonaMatch.Api.Authentication;
using ZonaMatch.Application.DTOs;
using ZonaMatch.Application.DTOs.Analysis.Requests;
using ZonaMatch.Application.DTOs.Analysis.Responses;
using ZonaMatch.Application.UseCases.AnalisisGuardados;

namespace ZonaMatch.Api.Controllers
{
    // Analisis guardados del usuario: zonas comparadas con la copia de su criterio, y sus versiones ajustadas
    [ApiController]
    [Route("[controller]")]
    [Authorize]
    public class AnalisisController : ControllerBase
    {
        private readonly ListarMisAnalisis _listarMisAnalisis;
        private readonly ObtenerAnalisis _obtenerAnalisis;
        private readonly CrearAnalisis _crearAnalisis;
        private readonly CrearVersionAnalisis _crearVersionAnalisis;
        private readonly RenombrarAnalisis _renombrarAnalisis;
        private readonly EliminarAnalisis _eliminarAnalisis;
        private readonly CompararAnalisis _compararAnalisis;

        public AnalisisController(
            ListarMisAnalisis listarMisAnalisis,
            ObtenerAnalisis obtenerAnalisis,
            CrearAnalisis crearAnalisis,
            CrearVersionAnalisis crearVersionAnalisis,
            RenombrarAnalisis renombrarAnalisis,
            EliminarAnalisis eliminarAnalisis,
            CompararAnalisis compararAnalisis)
        {
            _listarMisAnalisis = listarMisAnalisis;
            _obtenerAnalisis = obtenerAnalisis;
            _crearAnalisis = crearAnalisis;
            _crearVersionAnalisis = crearVersionAnalisis;
            _renombrarAnalisis = renombrarAnalisis;
            _eliminarAnalisis = eliminarAnalisis;
            _compararAnalisis = compararAnalisis;
        }

        // GET /Analisis — originales, cada uno con sus versiones, el mas reciente primero
        [HttpGet]
        [ProducesResponseType<IReadOnlyList<AnalysisResponse>>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Listar(CancellationToken cancellationToken)
        {
            if (User.UsuarioId() is not { } usuarioId)
                return Unauthorized();

            return Ok(await _listarMisAnalisis.EjecutarAsync(usuarioId, cancellationToken));
        }

        // POST /Analisis/Comparar — recalcula las zonas guardadas con cada análisis elegido
        [HttpPost("Comparar")]
        [ProducesResponseType<ComparacionAnalisisDto>(StatusCodes.Status200OK)]
        [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Comparar(CompararAnalisisRequest solicitud, CancellationToken cancellationToken)
        {
            if (User.UsuarioId() is not { } usuarioId)
                return Unauthorized();

            return Ok(await _compararAnalisis.EjecutarAsync(usuarioId, solicitud, cancellationToken));
        }

        // GET /Analisis/{id}
        [HttpGet("{id:guid}")]
        [ProducesResponseType<AnalysisResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Obtener(Guid id, CancellationToken cancellationToken)
        {
            if (User.UsuarioId() is not { } usuarioId)
                return Unauthorized();

            return Ok(await _obtenerAnalisis.EjecutarAsync(id, usuarioId, cancellationToken));
        }

        // POST /Analisis
        // Body: { "nombre": "Cerca del trabajo", "zonas": [{ "slug": "villa-luro", "nombre": "Villa Luro, CABA", "esInicial": true }] }
        // El criterio se copia del perfil (404 si el usuario todavia no lo armo)
        [HttpPost]
        [ProducesResponseType<AnalysisResponse>(StatusCodes.Status201Created)]
        [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Crear(CreateAnalysisRequest solicitud, CancellationToken cancellationToken)
        {
            if (User.UsuarioId() is not { } usuarioId)
                return Unauthorized();

            var analisis = await _crearAnalisis.EjecutarAsync(usuarioId, solicitud, cancellationToken);
            return CreatedAtAction(nameof(Obtener), new { id = analisis.Id }, analisis);
        }

        // POST /Analisis/{id}/versiones
        // Body: { "nombre": null, "criterio": { "contexto": {}, "criterios": {}, "puntos": {} }, "zonas": null }
        // Guarda una version con el criterio ajustado; cuelga del original aunque {id} sea otra version
        [HttpPost("{id:guid}/versiones")]
        [ProducesResponseType<AnalysisResponse>(StatusCodes.Status201Created)]
        [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CrearVersion(Guid id, CreateAnalysisVersionRequest solicitud, CancellationToken cancellationToken)
        {
            if (User.UsuarioId() is not { } usuarioId)
                return Unauthorized();

            var version = await _crearVersionAnalisis.EjecutarAsync(id, usuarioId, solicitud, cancellationToken);
            return CreatedAtAction(nameof(Obtener), new { id = version.Id }, version);
        }

        // PUT /Analisis/{id} — Body: { "nombre": "..." }
        [HttpPut("{id:guid}")]
        [ProducesResponseType<AnalysisResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Renombrar(Guid id, RenameAnalysisRequest solicitud, CancellationToken cancellationToken)
        {
            if (User.UsuarioId() is not { } usuarioId)
                return Unauthorized();

            return Ok(await _renombrarAnalisis.EjecutarAsync(id, usuarioId, solicitud, cancellationToken));
        }

        // DELETE /Analisis/{id} — borrar un original borra sus versiones
        [HttpDelete("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Eliminar(Guid id, CancellationToken cancellationToken)
        {
            if (User.UsuarioId() is not { } usuarioId)
                return Unauthorized();

            await _eliminarAnalisis.EjecutarAsync(id, usuarioId, cancellationToken);
            return NoContent();
        }
    }
}
