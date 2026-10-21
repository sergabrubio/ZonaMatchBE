namespace ZonaMatch.Domain.Entities
{
    // Zona comparada en un analisis (tabla app.analisis_zonas). Las zonas no son una tabla:
    // se referencian por slug, como en la comunidad.
    public class AnalisisZona
    {
        public Guid AnalisisId { get; set; }

        // Posicion en la lista del usuario (1, 2, ...)
        public int Orden { get; set; }

        public string ZonaSlug { get; set; } = string.Empty;

        // Nombre como lo vio el usuario al elegirla ("Villa Luro, CABA"), para listar sin consultar las zonas
        public string Nombre { get; set; } = string.Empty;

        // La zona de partida que eligio en el asistente
        public bool EsInicial { get; set; }

        // Zona añadida manualmente al ranking automático.
        public bool EsAgregada { get; set; }
    }
}
