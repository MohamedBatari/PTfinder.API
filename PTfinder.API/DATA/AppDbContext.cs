using Microsoft.EntityFrameworkCore;
using PTfinder.API.DATA.Modules;
using PTfinder.API.Models;
using Stripe;

namespace PTfinder.API.DATA
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        public DbSet<Partner> Partners { get; set; }

        public DbSet<Category> Categories { get; set; } = null!;
        public DbSet<Speciality> Specialities { get; set; } = null!;
        public DbSet<Coach> Coaches { get; set; } = null!;
        public DbSet<Availability> Availabilities { get; set; } = null!;
        public DbSet<Booking> Bookings { get; set; } = null!;
        public DbSet<Review> Reviews { get; set; } = null!;
        public DbSet<Country> Countries { get; set; } = null!;
        public DbSet<City> Cities { get; set; } = null!;
        public DbSet<Area> Areas { get; set; } = null!;
        public DbSet<GalleryMedia> GalleryMedia { get; set; } = null!;
        public DbSet<EmailOtp> EmailOtps { get; set; } = default!;
        public DbSet<Notification> Notifications { get; set; } = null!;
        public DbSet<CoachGift> CoachGifts { get; set; } = null!;

        public DbSet<Client> Clients { get; set; }
        public DbSet<ClientContactView> ClientContactViews { get; set; }
        public DbSet<CoachProfileView> CoachProfileViews { get; set; }
        public DbSet<Conversation> Conversations { get; set; } = null!;
        public DbSet<ConversationMessage> ConversationMessages { get; set; } = null!;
        public DbSet<PushDevice> PushDevices { get; set; } = null!;



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {

            modelBuilder.Entity<Availability>(b =>
            {
                b.HasIndex(a => new { a.CoachId, a.AvailableDate, a.TimeSlot })
                 .IsUnique();
            });

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

            modelBuilder.Entity<EmailOtp>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Email).IsRequired().HasMaxLength(320);
                b.Property(x => x.CodeHash).IsRequired().HasMaxLength(64); // SHA256 hex
                b.Property(x => x.Attempts).HasDefaultValue(0);

                b.HasIndex(x => x.Email);
                b.HasIndex(x => new { x.Email, x.CodeHash });
                b.HasIndex(x => x.ExpiresUtc);
            });

            modelBuilder.Entity<Client>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.HasIndex(x => x.Email).IsUnique();
                entity.HasIndex(x => x.GoogleSub).IsUnique();

                entity.Property(x => x.Email).HasMaxLength(256).IsRequired();
                entity.Property(x => x.GoogleSub).HasMaxLength(128).IsRequired();
                entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
                entity.Property(x => x.PictureUrl).HasMaxLength(1000);
                entity.Property(x => x.LastIpAddress).HasMaxLength(100);
                entity.Property(x => x.LastUserAgent).HasMaxLength(1000);
                entity.Property(x => x.ClientTimeZone).HasMaxLength(100);
                entity.Property(x => x.TermsVersion).HasMaxLength(50);
                entity.Property(x => x.PrivacyVersion).HasMaxLength(50);
            });

            modelBuilder.Entity<ClientContactView>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.ActionType).HasMaxLength(50).IsRequired();
                entity.Property(x => x.IpAddress).HasMaxLength(100);
                entity.Property(x => x.UserAgent).HasMaxLength(1000);
                entity.Property(x => x.Referrer).HasMaxLength(1000);
                entity.Property(x => x.ClientTimeZone).HasMaxLength(100);

                entity.HasOne(x => x.Client)
                    .WithMany(x => x.ContactViews)
                    .HasForeignKey(x => x.ClientId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.Coach)
                    .WithMany()
                    .HasForeignKey(x => x.CoachId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => new { x.ClientId, x.CoachId, x.CreatedAtUtc });
            });

            modelBuilder.Entity<Conversation>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.HasIndex(x => new { x.CoachId, x.ClientId }).IsUnique();
                entity.HasIndex(x => new { x.CoachId, x.LastMessageAtUtc });
                entity.HasIndex(x => new { x.ClientId, x.LastMessageAtUtc });

                entity.HasOne(x => x.Coach)
                    .WithMany()
                    .HasForeignKey(x => x.CoachId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.Client)
                    .WithMany()
                    .HasForeignKey(x => x.ClientId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ConversationMessage>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Body).IsRequired().HasMaxLength(2000);
                entity.HasIndex(x => new { x.ConversationId, x.CreatedAtUtc });
                entity.HasOne(x => x.Conversation)
                    .WithMany(x => x.Messages)
                    .HasForeignKey(x => x.ConversationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });


            // DATA/AppDbContext.cs  inside OnModelCreating
            modelBuilder.Entity<Notification>(e =>
            {
                e.Property(x => x.Title).HasMaxLength(200).IsRequired();
                e.Property(x => x.Body).HasMaxLength(2000).IsRequired();
                e.Property(x => x.Link).HasMaxLength(512);
                e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");
            });

            modelBuilder.Entity<PushDevice>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Token).HasMaxLength(512).IsRequired();
                e.Property(x => x.Provider).HasMaxLength(40).IsRequired();
                e.Property(x => x.Platform).HasMaxLength(20).IsRequired();
                e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("GETUTCDATE()");
                e.Property(x => x.LastSeenAtUtc).HasDefaultValueSql("GETUTCDATE()");
                e.HasIndex(x => x.Token).IsUnique();
                e.HasIndex(x => new { x.CoachId, x.IsActive });
                e.HasIndex(x => new { x.ClientId, x.IsActive });
            });
            // Gifts
            modelBuilder.Entity<CoachGift>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.AmountMinor).IsRequired();
                e.Property(x => x.Currency).HasMaxLength(10).IsRequired();
                e.Property(x => x.Note).HasMaxLength(200);
                e.Property(x => x.StripeSessionId).HasMaxLength(200).IsRequired();
                e.Property(x => x.StripePaymentIntentId).HasMaxLength(200);
                e.Property(x => x.Status).HasMaxLength(40).IsRequired();
                e.Property(x => x.DonorEmail).HasMaxLength(320);
                e.Property(x => x.CreatedUtc).HasDefaultValueSql("GETUTCDATE()");

                e.HasIndex(x => x.CoachId);
                e.HasIndex(x => x.CreatedUtc);

                e.HasOne(x => x.Coach)
                    .WithMany() // or .WithMany(c => c.Gifts) if you add a collection on Coach
                    .HasForeignKey(x => x.CoachId)
                    .OnDelete(DeleteBehavior.Cascade);
            });


            modelBuilder.Entity<Coach>()
    .HasOne(c => c.Partner)
    .WithMany(p => p.Coaches)
    .HasForeignKey(c => c.PartnerId)
    .OnDelete(DeleteBehavior.SetNull); // if partner deleted, keep coach as freelancer

            // Useful indexes
            modelBuilder.Entity<Coach>().HasIndex(c => c.PartnerId);
            modelBuilder.Entity<Coach>().HasIndex(c => c.Email);

            modelBuilder.Ignore<StripeResponse>();
            modelBuilder.Ignore<Subscription>();





            base.OnModelCreating(modelBuilder);

            // DO NOT add any global override loop here that changes all DeleteBehavior.
        }
    }
}


