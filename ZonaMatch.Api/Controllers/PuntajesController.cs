using Microsoft.AspNetCore.Mvc;
using ZonaMatch.Application.DTOs;
using ZonaMatch.Application.UseCases.Puntajes;

namespace ZonaMatch.Api.Controllers
{
    // Motor de puntaje: que tanto le cierra cada zona a un usuario segun su criterio.
    // Publico: el visitante tambien ve el resultado de su analisis (el criterio viaja en el pedido).
    [ApiController]
    [Route("[controller]")]
    public class PuntajesController : ControllerBase
    {
        private readonly CalcularPuntajes _calcularPuntajes;

        public PuntajesController(CalcularPuntajes calcularPuntajes)
        {
            _calcularPuntajes = calcularPuntajes;
        }

        // POST /Puntajes
        // Body: { "zonaInicial": "villa-luro", "zonasAgregadas": [], "criterios": { "seguridad": "alta" },
        //         "lugares": [{ "nombre": "Trabajo", "latitude": -34.6, "longitude": -58.38, "prioridad": "Alta", "tiempoMax": "45 min" }],
        //         "alquilerMaximo": "800000", "movilidad": ["transporte-publico"] }
        [HttpPost]
        [ProducesResponseType<PuntajesDto>(StatusCodes.Status200OK)]
        [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Calcular(CalcularPuntajesRequest solicitud, CancellationToken cancellationToken) =>
            Ok(await _calcularPuntajes.EjecutarAsync(solicitud, cancellationToken));
    }
}
