using System.Text.Json;
using System.Text.Json.Nodes;
using ZonaMatch.Application.DTOs.GroupAnalysis.Responses;

namespace ZonaMatch.Application.UseCases.AnalisisGuardados;

/// <summary>
/// Reads ranking snapshots saved before the group DTO fields were standardized in English.
/// </summary>
internal static class GroupRankingSnapshotJson
{
    private static readonly IReadOnlyDictionary<string, string> LegacyPropertyNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["zonas"] = "zones",
        ["participantesExcluidos"] = "excludedParticipants",
        ["puntosParticipantes"] = "participantPoints",
        ["alquilerMaximo"] = "maximumRent",
        ["cercanas"] = "nearbyZones",
        ["nombre"] = "name",
        ["motivo"] = "reason",
        ["participanteNombre"] = "participantName",
        ["direccion"] = "address",
        ["tipo"] = "type",
        ["prioridad"] = "priority",
        ["tiempoMax"] = "maximumTime",
        ["puntaje"] = "score",
        ["participantes"] = "participants",
        ["componentes"] = "components",
        ["limites"] = "limits",
        ["lugares"] = "places",
        ["alquiler"] = "rent",
        ["criterio"] = "criterion",
        ["detalle"] = "details",
        ["peso"] = "weight",
        ["pesoNormalizado"] = "normalizedWeight",
        ["cumple"] = "isMet",
        ["modoViaje"] = "travelMode",
        ["minutos"] = "minutes",
        ["maximoMinutos"] = "maximumMinutes",
        ["estimado"] = "isEstimated",
        ["promedio"] = "average",
        ["propiedadesDisponibles"] = "availableProperties",
    };

    public static GroupRankingResponse? Deserialize(string json, JsonSerializerOptions options)
    {
        var node = JsonNode.Parse(json);
        NormalizeLegacyNames(node);
        return node?.Deserialize<GroupRankingResponse>(options);
    }

    private static void NormalizeLegacyNames(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToList())
            {
                NormalizeLegacyNames(property.Value);
                if (LegacyPropertyNames.TryGetValue(property.Key, out var englishName))
                {
                    var normalizedValue = property.Value?.DeepClone();
                    jsonObject.Remove(property.Key);
                    jsonObject[englishName] = normalizedValue;
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var child in jsonArray)
                NormalizeLegacyNames(child);
        }
    }
}
