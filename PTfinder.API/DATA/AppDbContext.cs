using System.Linq;
using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA.Modules;

namespace PTfinder.API.DATA
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Category> Categories { get; set; } = null!;
        public DbSet<Speciality> Specialities { get; set; } = null!;
        public DbSet<Coach> Coaches { get; set; } = null!;
        public DbSet<Availability> Availabilities { get; set; } = null!;
        public DbSet<Booking> Bookings { get; set; } = null!;
        public DbSet<Review> Reviews { get; set; } = null!;
        public DbSet<Subscription> Subscriptions { get; set; } = null!;
        public DbSet<Country> Countries { get; set; } = null!;
        public DbSet<City> Cities { get; set; } = null!;
        public DbSet<Area> Areas { get; set; } = null!;
        public DbSet<GalleryMedia> GalleryMedia { get; set; } = null!;

        public DbSet<EmailVerification> EmailVerifications { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Relationships you explicitly want
            modelBuilder.Entity<Category>()
                .HasMany(c => c.Specialities)
                .WithOne(s => s.Category)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Category>()
                .HasMany(c => c.Coaches)
                .WithOne(c => c.Category)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Speciality>()
                .HasMany(s => s.Coaches)
                .WithOne(c => c.Speciality)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Country>()
                .HasMany(c => c.Cities)
                .WithOne(c => c.Country)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<City>()
                .HasMany(c => c.Areas)
                .WithOne(a => a.City)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Coach>()
                .Property(c => c.Price)
                .HasPrecision(10, 2);

            // EmailVerification: lengths + indexes so SQL Server can index
            modelBuilder.Entity<EmailVerification>(b =>
            {
                b.Property(x => x.Email).IsRequired().HasMaxLength(320);
                b.Property(x => x.Token).IsRequired().HasMaxLength(200);

                b.HasIndex(x => x.Email);
                b.HasIndex(x => x.Token).IsUnique();
            });

            base.OnModelCreating(modelBuilder);

            // IMPORTANT: Do NOT blanket-force Restrict on all FKs here,
            // or you’ll override the cascades defined above.
            // If you *really* want a default, you can selectively adjust only ClientCascade:
            //
            // foreach (var fk in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            // {
            //     if (fk.DeleteBehavior == DeleteBehavior.ClientCascade)
            //         fk.DeleteBehavior = DeleteBehavior.Restrict;
            // }
        }
    }
}

