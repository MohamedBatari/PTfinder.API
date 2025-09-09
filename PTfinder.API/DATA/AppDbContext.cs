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
        public DbSet<EmailOtp> EmailOtps { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Safe cascades for the location hierarchy
            modelBuilder.Entity<Country>()
                .HasMany(c => c.Cities)
                .WithOne(c => c.Country)
                .HasForeignKey(c => c.CountryId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<City>()
                .HasMany(c => c.Areas)
                .WithOne(a => a.City)
                .HasForeignKey(a => a.CityId)
                .OnDelete(DeleteBehavior.Cascade);

            // Category ↔ Speciality and Category ↔ Coach
            modelBuilder.Entity<Category>()
                .HasMany(c => c.Specialities)
                .WithOne(s => s.Category)
                .HasForeignKey(s => s.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Category>()
                .HasMany(c => c.Coaches)
                .WithOne(c => c.Category)
                .HasForeignKey(c => c.CategoryId)
                .OnDelete(DeleteBehavior.NoAction); // break cascades here

            modelBuilder.Entity<Speciality>()
                .HasMany(s => s.Coaches)
                .WithOne(c => c.Speciality)
                .HasForeignKey(c => c.SpecialityId)
                .OnDelete(DeleteBehavior.NoAction);   // break cascades here

            // Coach → Country/City/Area must NOT cascade
            modelBuilder.Entity<Coach>()
                .HasOne(c => c.Country)
                .WithMany()
                .HasForeignKey(c => c.CountryId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Coach>()
                .HasOne(c => c.City)
                .WithMany()
                .HasForeignKey(c => c.CityId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Coach>()
                .HasOne(c => c.Area)
                .WithMany()
                .HasForeignKey(c => c.AreaId)
                .OnDelete(DeleteBehavior.NoAction);

            // Price precision
            modelBuilder.Entity<Coach>()
                .Property(c => c.Price)
                .HasPrecision(10, 2);

           
            base.OnModelCreating(modelBuilder);

            // DO NOT add any global override loop here that changes all DeleteBehavior.
        }
    }
}


