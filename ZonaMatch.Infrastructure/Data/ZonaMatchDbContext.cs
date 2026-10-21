using Microsoft.EntityFrameworkCore;
using ZonaMatch.Domain.Common;
using ZonaMatch.Domain.Entities;

namespace ZonaMatch.Infrastructure.Data
{
    // Entities of the "geo" schema (external GIS data, see zonamatch.sql) are mapped here but excluded from
    // migrations: they are loaded from the dump, not created by EF Core. Tables of the application model go to "app".
    public class ZonaMatchDbContext : DbContext
    {
        public ZonaMatchDbContext(DbContextOptions<ZonaMatchDbContext> options)
            : base(options)
        {
        }

        public DbSet<Escuela> Escuelas => Set<Escuela>();
        public DbSet<Partido> Partidos => Set<Partido>();
        public DbSet<Comuna> Comunas => Set<Comuna>();
        public DbSet<Radio> Radios => Set<Radio>();
        public DbSet<MunicipioAlias> MunicipiosAlias => Set<MunicipioAlias>();
        public DbSet<Usuario> Usuarios => Set<Usuario>();
        public DbSet<Resena> Resenas => Set<Resena>();
        public DbSet<ResenaVotoUtil> ResenasVotosUtil => Set<ResenaVotoUtil>();
        public DbSet<Pregunta> Preguntas => Set<Pregunta>();
        public DbSet<Respuesta> Respuestas => Set<Respuesta>();
        public DbSet<Aporte> Aportes => Set<Aporte>();
        public DbSet<AporteValidacion> AportesValidaciones => Set<AporteValidacion>();
        public DbSet<Group> Groups => Set<Group>();
        public DbSet<GroupInvitation> GroupInvitations => Set<GroupInvitation>();
        public DbSet<Contexto> Contextos => Set<Contexto>();
        public DbSet<Analisis> Analisis => Set<Analisis>();
        public DbSet<AnalisisZona> AnalisisZonas => Set<AnalisisZona>();
        public DbSet<Persona> Personas => Set<Persona>();
        public DbSet<PuntoDeInteres> PuntosInteres => Set<PuntoDeInteres>();
        public DbSet<ContextoCriterio> ContextoCriterios => Set<ContextoCriterio>();
        public DbSet<Criterio> Criterios => Set<Criterio>();
        public DbSet<AlquilerZona> AlquileresZona => Set<AlquilerZona>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.HasDefaultSchema(ZonaMatchDbOptions.AppSchema);

            // The dump uses snake_case columns. Applied first so the explicit names below can override it.
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
                property.SetColumnName(ToSnakeCase(property.Name));

            modelBuilder.Entity<Escuela>(e =>
            {
                e.ToTable("escuelas", ZonaMatchDbOptions.GeoSchema, t => t.ExcludeFromMigrations());
                e.HasKey(x => x.ClaveNatural);
                e.Property(x => x.Ubicacion).HasColumnName("geom").HasColumnType("geometry(Point,4326)");
            });

            modelBuilder.Entity<Partido>(e =>
            {
                e.ToTable("partidos", ZonaMatchDbOptions.GeoSchema, t => t.ExcludeFromMigrations());
                e.HasKey(x => x.Key);
                e.Property(x => x.Geometria).HasColumnName("geom").HasColumnType("geometry(MultiPolygon,4326)");
            });

            modelBuilder.Entity<Comuna>(e =>
            {
                e.ToTable("comunas", ZonaMatchDbOptions.GeoSchema, t => t.ExcludeFromMigrations());
                e.HasKey(x => x.Key);
                e.Property(x => x.Geometria).HasColumnName("geom").HasColumnType("geometry(MultiPolygon,4326)");
            });

            modelBuilder.Entity<Radio>(e =>
            {
                e.ToTable("radios", ZonaMatchDbOptions.GeoSchema, t => t.ExcludeFromMigrations());
                e.HasKey(x => x.Id);
                e.Property(x => x.NroRadio).HasColumnName("radio");
                e.Property(x => x.Geometria).HasColumnName("geom").HasColumnType("geometry(MultiPolygon,4326)");
            });

            modelBuilder.Entity<MunicipioAlias>(e =>
            {
                e.ToTable("municipio_alias", ZonaMatchDbOptions.GeoSchema, t => t.ExcludeFromMigrations());
                e.HasKey(x => x.MunicipioNombre);

                e.HasOne(x => x.Partido)
                    .WithMany(p => p.Aliases)
                    .HasForeignKey(x => x.PartidoKey);
            });

            // Application model: created by migrations in the "app" schema
            modelBuilder.Entity<Usuario>(e =>
            {
                e.ToTable("usuarios");
                e.HasKey(x => x.Id);
                e.Property(x => x.Email).HasMaxLength(254).IsRequired();
                e.HasIndex(x => x.Email).IsUnique();
                e.Property(x => x.PasswordHash).HasMaxLength(255).IsRequired();
                e.Property(x => x.FechaNacimiento).HasColumnType("date");
                e.Property(x => x.FechaCreacion).HasColumnType("timestamptz");
            });

            // Criterio de busqueda: el perfil del usuario o la copia guardada con un analisis
            modelBuilder.Entity<Contexto>(e =>
            {
                e.ToTable("contextos", t =>
                    t.HasCheckConstraint("ck_contextos_tipo", "tipo IN ('perfil', 'analisis')"));
                e.HasKey(x => x.Id);
                e.Property(x => x.Tipo).HasMaxLength(10).IsRequired();
                e.Property(x => x.Personas).IsRequired();
                e.Property(x => x.Mascotas).HasColumnType("text[]");
                e.Property(x => x.Movilidad).HasColumnType("text[]");
                e.Property(x => x.AlquilerMaximo).HasMaxLength(30);
                e.Property(x => x.FechaCreacion).HasColumnType("timestamptz");
                e.Property(x => x.FechaActualizacion).HasColumnType("timestamptz");
                // Un solo perfil por usuario; copias de analisis puede tener todas las que quiera
                e.HasIndex(x => x.UsuarioId)
                    .IsUnique()
                    .HasFilter("tipo = 'perfil'")
                    .HasDatabaseName("ux_contextos_perfil_usuario");
                e.HasOne<Usuario>()
                    .WithMany()
                    .HasForeignKey(x => x.UsuarioId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.Integrantes)
                    .WithOne(p => p.Contexto)
                    .HasForeignKey(p => p.ContextoId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.Puntos)
                    .WithOne(p => p.Contexto)
                    .HasForeignKey(p => p.ContextoId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.Criterios)
                    .WithOne()
                    .HasForeignKey(x => x.ContextoId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Analisis guardado: zonas comparadas + la copia del criterio con que se calculo
            modelBuilder.Entity<Analisis>(e =>
            {
                e.ToTable("analisis");
                e.HasKey(x => x.Id);
                e.Property(x => x.Nombre).HasMaxLength(50).IsRequired();
                e.Property(x => x.PuntajesEnGrupoJson).HasColumnType("jsonb");
                e.Property(x => x.FechaCreacion).HasColumnType("timestamptz");
                e.Property(x => x.FechaActualizacion).HasColumnType("timestamptz");
                e.HasIndex(x => x.UsuarioId);
                e.HasIndex(x => x.AnalisisOrigenId);
                e.HasIndex(x => x.ContextoId).IsUnique();
                e.HasOne<Usuario>()
                    .WithMany()
                    .HasForeignKey(x => x.UsuarioId)
                    .OnDelete(DeleteBehavior.Cascade);
                // Cada analisis tiene su propia copia del criterio; se borran juntos (ver EliminarAnalisis)
                e.HasOne(x => x.Contexto)
                    .WithMany()
                    .HasForeignKey(x => x.ContextoId)
                    .OnDelete(DeleteBehavior.Cascade);
                // Borrar un original borra sus versiones
                e.HasOne<Analisis>()
                    .WithMany()
                    .HasForeignKey(x => x.AnalisisOrigenId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.Zonas)
                    .WithOne()
                    .HasForeignKey(x => x.AnalisisId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AnalisisZona>(e =>
            {
                e.ToTable("analisis_zonas");
                e.HasKey(x => new { x.AnalisisId, x.Orden });
                e.Property(x => x.ZonaSlug).HasMaxLength(100).IsRequired();
                e.Property(x => x.Nombre).HasMaxLength(150).IsRequired();
                e.Property(x => x.EsAgregada).HasDefaultValue(false);
            });

            modelBuilder.Entity<Persona>(e =>
            {
                e.ToTable("personas");
                e.HasKey(x => x.Id);
                e.Property(x => x.Orden);
                e.Property(x => x.Nombre).HasMaxLength(50);
                e.Property(x => x.Edad).HasMaxLength(40);
                e.HasIndex(x => new { x.ContextoId, x.Orden });
                e.HasOne(x => x.Contexto)
                    .WithMany(c => c.Integrantes)
                    .HasForeignKey(x => x.ContextoId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PuntoDeInteres>(e =>
            {
                e.ToTable("puntos_interes");
                e.HasKey(x => x.Id);
                e.Property(x => x.Orden);
                e.Property(x => x.Nombre).HasMaxLength(100).IsRequired();
                e.Property(x => x.Direccion).HasMaxLength(200);
                e.Property(x => x.Tipo).HasMaxLength(30);
                e.Property(x => x.QuienVa).HasMaxLength(20);
                e.Property(x => x.Frecuencia).HasMaxLength(20);
                e.Property(x => x.Prioridad).HasMaxLength(20);
                e.Property(x => x.TiempoMax).HasMaxLength(10);
                e.HasIndex(x => new { x.ContextoId, x.Orden });
                e.HasOne(x => x.Contexto)
                    .WithMany(c => c.Puntos)
                    .HasForeignKey(x => x.ContextoId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ContextoCriterio>(e =>
            {
                e.ToTable("contexto_criterios", t =>
                    t.HasCheckConstraint("ck_contexto_criterios_prioridad", "prioridad IN ('baja', 'media', 'alta')"));
                e.HasKey(x => new { x.ContextoId, x.CriterioId });
                e.Property(x => x.CriterioId).HasMaxLength(40);
                e.Property(x => x.Prioridad).HasMaxLength(10).IsRequired();
                e.HasOne(x => x.Contexto)
                    .WithMany(c => c.Criterios)
                    .HasForeignKey(x => x.ContextoId)
                    .OnDelete(DeleteBehavior.Cascade);
                // Un criterio borrado del catalogo no deberia existir, pero si pasa:
                // la fila del analisis no se toca
                e.HasOne(x => x.Criterio)
                    .WithMany()
                    .HasForeignKey(x => x.CriterioId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Criterio>(e =>
            {
                e.ToTable("criterios");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasMaxLength(40);
                e.Property(x => x.Nombre).HasMaxLength(50).IsRequired();
                e.Property(x => x.Descripcion).HasMaxLength(200).IsRequired();
                e.Property(x => x.Icono).HasMaxLength(40).IsRequired();
                e.Property(x => x.Orden);
                e.Property(x => x.Activo);
                e.HasIndex(x => x.Orden);
            });

            // Temporal: alquiler promedio por zona base hasta que lo llene el scraping de Argenprop
            modelBuilder.Entity<AlquilerZona>(e =>
            {
                e.ToTable("alquileres_zona", t =>
                {
                    t.HasCheckConstraint("ck_alquileres_zona_promedio", "alquiler_promedio >= 0");
                    t.HasCheckConstraint("ck_alquileres_zona_propiedades", "propiedades_disponibles >= 0");
                });
                e.HasKey(x => x.ZonaSlug);
                e.Property(x => x.ZonaSlug).HasMaxLength(100);
                e.Property(x => x.AlquilerPromedio).HasColumnType("numeric(12,0)");
                e.Property(x => x.Fuente).HasMaxLength(30).IsRequired();
                e.Property(x => x.FechaActualizacion).HasColumnType("timestamptz");
            });

            // Comunidad de una zona (resenas, preguntas y aportes). Las zonas no son una tabla: se referencian
            // por slug (ver GET /Zonas/{slug}).
            modelBuilder.Entity<Resena>(e =>
            {
                e.ToTable("resenas", t =>
                {
                    t.HasCheckConstraint("ck_resenas_puntajes",
                        "puntaje_seguridad BETWEEN 1 AND 5 AND puntaje_transporte BETWEEN 1 AND 5 AND puntaje_conectividad BETWEEN 1 AND 5 " +
                        "AND puntaje_comercios BETWEEN 1 AND 5 AND puntaje_espacios_verdes BETWEEN 1 AND 5");
                });
                e.HasKey(x => x.Id);
                e.Property(x => x.ZonaSlug).HasMaxLength(200);
                e.HasIndex(x => new { x.ZonaSlug, x.UsuarioId }).IsUnique();
                e.Property(x => x.Texto).HasMaxLength(Longitudes.TextoResena);
                e.Property(x => x.Temas).HasColumnType("text[]");
                e.Property(x => x.FechaCreacion).HasColumnType("timestamptz");
                e.Property(x => x.FechaActualizacion).HasColumnType("timestamptz");
                e.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.VotosUtil).WithOne().HasForeignKey(x => x.ResenaId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ResenaVotoUtil>(e =>
            {
                e.ToTable("resenas_votos_util");
                e.HasKey(x => new { x.ResenaId, x.UsuarioId });
                e.Property(x => x.Fecha).HasColumnType("timestamptz");
                e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Pregunta>(e =>
            {
                e.ToTable("preguntas");
                e.HasKey(x => x.Id);
                e.Property(x => x.ZonaSlug).HasMaxLength(200);
                e.HasIndex(x => x.ZonaSlug);
                e.Property(x => x.Texto).HasMaxLength(Longitudes.TextoPregunta);
                e.Property(x => x.FechaCreacion).HasColumnType("timestamptz");
                e.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.Respuestas).WithOne().HasForeignKey(x => x.PreguntaId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Respuesta>(e =>
            {
                e.ToTable("respuestas");
                e.HasKey(x => x.Id);
                e.Property(x => x.Texto).HasMaxLength(Longitudes.TextoRespuesta);
                e.Property(x => x.FechaCreacion).HasColumnType("timestamptz");
                e.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Aporte>(e =>
            {
                e.ToTable("aportes");
                e.HasKey(x => x.Id);
                e.Property(x => x.ZonaSlug).HasMaxLength(200);
                e.HasIndex(x => new { x.ZonaSlug, x.Estado });
                // Las consultas de puntos de interes buscan las correcciones aprobadas por lugar
                e.HasIndex(x => new { x.PuntoInteresId, x.Estado });
                // Enums como texto: legibles en la base y usables desde el SQL del mapa
                e.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
                e.Property(x => x.Estado).HasConversion<string>().HasMaxLength(20);
                e.Property(x => x.Motivo).HasConversion<string>().HasMaxLength(20);
                e.Property(x => x.Nombre).HasMaxLength(Longitudes.NombreLugar);
                e.Property(x => x.Categoria).HasMaxLength(40);
                e.Property(x => x.Ubicacion).HasColumnType("geometry(Point,4326)");
                e.Property(x => x.Horario).HasMaxLength(Longitudes.Horario);
                e.Property(x => x.Direccion).HasMaxLength(Longitudes.Direccion);
                e.Property(x => x.PuntoInteresId).HasMaxLength(40);
                e.Property(x => x.PuntoInteresNombre).HasMaxLength(Longitudes.Direccion);
                e.Property(x => x.Comentario).HasMaxLength(Longitudes.Comentario);
                e.Property(x => x.FechaCreacion).HasColumnType("timestamptz");
                e.Property(x => x.FechaResolucion).HasColumnType("timestamptz");
                e.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.Validaciones).WithOne().HasForeignKey(x => x.AporteId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AporteValidacion>(e =>
            {
                e.ToTable("aportes_validaciones");
                e.HasKey(x => new { x.AporteId, x.UsuarioId });
                e.Property(x => x.Fecha).HasColumnType("timestamptz");
                e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<GroupInvitation>()
                .Property(i => i.Status)
                .HasConversion<string>();

            modelBuilder.Entity<Participant>()
                .Property(p => p.Role)
                .HasConversion<string>();
        }

        private static string ToSnakeCase(string name) =>
            string.Concat(name.Select((c, i) =>
                i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }
}
