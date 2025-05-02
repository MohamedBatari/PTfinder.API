using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA.Modules;

namespace PTfinder.API.DATA
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Category> Categories { get; set; }
        public DbSet<Speciality> Specialities { get; set; }
        public DbSet<Coach> Coaches { get; set; }
        public DbSet<Availability> Availabilities { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<Review> Reviews { get; set; }
        public DbSet<Subscription> Subscriptions { get; set; }
        public DbSet<Country> Countries { get; set; }
        public DbSet<City> Cities { get; set; }
        public DbSet<Area> Areas { get; set; }
        public DbSet<GalleryMedia> GalleryMedia { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
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

            base.OnModelCreating(modelBuilder);

            foreach (var relationship in modelBuilder.Model.GetEntityTypes()
                .SelectMany(e => e.GetForeignKeys()))
            {
                relationship.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }
    }

}
