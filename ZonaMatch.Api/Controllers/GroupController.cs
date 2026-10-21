using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using ZonaMatch.Api.Authentication;
using ZonaMatch.Application.DTOs;
using ZonaMatch.Application.DTOs.Analysis.Responses;
using ZonaMatch.Application.DTOs.Group;
using ZonaMatch.Application.DTOs.GroupAnalysis.Requests;
using ZonaMatch.Application.DTOs.GroupAnalysis.Responses;
using ZonaMatch.Application.UseCases.Groups;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace ZonaMatch.Api.Controllers
{
    [Route("[controller]")]
    [ApiController]
    [Authorize]
    public class GroupController : ControllerBase
    {

        private readonly CreateGroup _createGroup;
        private readonly GetGroupDetails _getGroupDetails;
        private readonly GetGroups _getGroups;
        private readonly DeleteGroup _deleteGroup;
        private readonly UpdateGroup _updateGroup;
        private readonly RemoveParticipant _removeParticipant;
        private readonly CalcularPuntajeEnGrupo _calcularPuntajeEnGrupo;
        private readonly GuardarAnalisisEnGrupo _guardarAnalisisEnGrupo;

        public GroupController(
            CreateGroup createGroup,
            GetGroupDetails getGroupDetails,
            GetGroups getGroups,
            DeleteGroup deleteGroup,
            UpdateGroup updateGroup,
            RemoveParticipant removeParticipant,
            CalcularPuntajeEnGrupo calcularPuntajeEnGrupo,
            GuardarAnalisisEnGrupo guardarAnalisisEnGrupo)
        {
            _createGroup = createGroup;
            _getGroupDetails = getGroupDetails;
            _getGroups = getGroups;
            _deleteGroup = deleteGroup;
            _updateGroup = updateGroup;
            _removeParticipant = removeParticipant;
            _calcularPuntajeEnGrupo = calcularPuntajeEnGrupo;
            _guardarAnalisisEnGrupo = guardarAnalisisEnGrupo;
        }

        // GET /Group/{groupId} — detalle del grupo para sus participantes
        [HttpGet("{groupId:guid}")]
        [ProducesResponseType<GroupDetailsResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetGroupDetails([FromRoute] Guid groupId, CancellationToken cancellationToken)
        {
            if (User.UsuarioId() is not { } userId)
                return Unauthorized();

            var response = await _getGroupDetails.ExecuteAsync(groupId, userId, cancellationToken);
            return Ok(response);
        }

        // POST /Group/{groupId}/CalcularPuntajeEnGrupo — calculates the automatic top five and recommendations.
        [HttpPost("{groupId:guid}/CalcularPuntajeEnGrupo")]
        [ProducesResponseType<GroupRankingResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CalcularPuntajeEnGrupo(
            [FromRoute] Guid groupId,
            [FromBody] CalculateGroupScoreRequest request,
            CancellationToken cancellationToken)
        {
            if (User.UsuarioId() is not { } userId)
                return Unauthorized();

            var response = await _calcularPuntajeEnGrupo.EjecutarAsync(groupId, userId, request, cancellationToken);
            return Ok(response);
        }

        // POST /Group/{groupId}/Analisis — guarda el resultado compartido en los análisis del usuario actual.
        [HttpPost("{groupId:guid}/Analisis")]
        [ProducesResponseType<AnalysisResponse>(StatusCodes.Status201Created)]
        [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GuardarAnalisisEnGrupo(
            [FromRoute] Guid groupId,
            [FromBody] SaveGroupAnalysisRequest request,
            CancellationToken cancellationToken)
        {
            if (User.UsuarioId() is not { } userId)
                return Unauthorized();

            var analysis = await _guardarAnalisisEnGrupo.EjecutarAsync(groupId, userId, request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, analysis);
        }

        // POST /Group
        // Body: { "name": "Búsqueda Capital" }
        [HttpPost("Create")]
        [ProducesResponseType<GroupResponse>(StatusCodes.Status201Created)]
        [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> CreateGroup(CreateGroupRequest request, CancellationToken cancellationToken)
        {
            // 1. Extraemos el ID del usuario del Token tal como lo hace tu compañero
            var idClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (!Guid.TryParse(idClaim, out var userId))
                return Unauthorized(new ProblemDetails { Title = "Usuario inválido." });

            // 2. Le pasamos el trabajo al Servicio
            var nuevoGrupo = await _createGroup.ExecuteAsync(request, userId, cancellationToken);

            // 3. Devolvemos 201 Created (Idealmente con la ruta GET para ver el grupo creado, pero Ok/Created está perfecto)
            return StatusCode(StatusCodes.Status201Created, nuevoGrupo);
        }

        // GET: api/<GroupController>
        [HttpGet("Get")]
        [ProducesResponseType<GroupListResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> GetGroups(CancellationToken cancellationToken)
        {
            // 1. Extraemos el ID del usuario del Token tal como lo hace tu compañero
            var idClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (!Guid.TryParse(idClaim, out var userId))
                return Unauthorized(new ProblemDetails { Title = "Usuario inválido." });

            // 2. Le pasamos el trabajo al Servicio
            var response = await _getGroups.ExecuteAsync(userId, cancellationToken);

            // 3. Devolvemos 200 OK
            return Ok(response);
        }


        [HttpPut("Update/{groupId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> UpdateGroup([FromRoute] Guid groupId, UpdateGroupRequest request, CancellationToken cancellationToken)
        {
            var idClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (!Guid.TryParse(idClaim, out var userId))
                return Unauthorized();

            try
            {
                await _updateGroup.ExecuteAsync(groupId, userId, request, cancellationToken);
                return NoContent();
            }
            catch (Exception ex) when (ex.Message == "Grupo no encontrado")
            {
                return NotFound(new ProblemDetails { Title = ex.Message });
            }
            catch (Exception ex) when (ex.Message == "No tienes permiso para modificar este grupo")
            {
                return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails { Title = ex.Message });
            }
        }

        [HttpDelete("Delete/{groupId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> DeleteGroup([FromRoute] Guid groupId, CancellationToken cancellationToken)
        {
            var idClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (!Guid.TryParse(idClaim, out var userId))
                return Unauthorized();

            try
            {
                await _deleteGroup.ExecuteAsync(groupId, userId, cancellationToken);
                return NoContent();
            }
            catch (Exception ex) when (ex.Message == "Grupo no encontrado")
            {
                return NotFound(new ProblemDetails { Title = ex.Message });
            }
            catch (Exception ex) when (ex.Message == "No tienes permiso para eliminar este grupo")
            {
                return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails { Title = ex.Message });
            }
        }

        // DELETE: api/Groups/{groupId}/Participants/{userIdToRemove}
        [HttpDelete("{groupId}/Participants/{userIdToRemove}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RemoveParticipant(Guid groupId, Guid userIdToRemove, CancellationToken cancellationToken)
        {
            // Obtienes el ID del usuario logueado desde el token JWT (ajusta esto a tu lógica actual)
            var idClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (!Guid.TryParse(idClaim, out var userId))
                return Unauthorized();

            await _removeParticipant.ExecuteAsync(groupId, userIdToRemove, userId, cancellationToken);

            return NoContent(); // 204: Todo salió bien, no hay contenido para devolver
        }

    }
}
