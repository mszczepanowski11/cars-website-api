using System;
using cars_website_api.CarsWebsite.Domain.Entities;
using CarsWebsite;
using Microsoft.EntityFrameworkCore;
using DriveType = cars_website_api.CarsWebsite.Domain.Entities.DriveType;

namespace CarsWebsite
{
    public class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<Advert> Adverts { get; set; }
        public DbSet<CarAdvert> CarAdverts { get; set; }
        public DbSet<AdvertImage> AdvertImages { get; set; }
        public DbSet<AdvertFeature> AdvertFeatures { get; set; }
        public DbSet<VehicleCategory> VehicleCategories { get; set; }
        public DbSet<FavoriteAdvert> FavoriteAdverts { get; set; }
        public DbSet<Brand> Brands { get; set; }
        public DbSet<Model> Models { get; set; }
        public DbSet<Generation> Generations { get; set; }
        public DbSet<EngineVersion> EngineVersions { get; set; }
        public DbSet<FuelType> FuelTypes { get; set; }
        public DbSet<Gearbox> Gearboxes { get; set; }
        public DbSet<BodyType> BodyTypes { get; set; }
        public DbSet<Feature> Features { get; set; }
        public DbSet<FeatureCategory> FeatureCategories { get; set; }
        
        public DbSet<Conversation> Conversations { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<Report> Reports { get; set; }
        public DbSet<AdminActionLog> AdminActionLogs { get; set; }
        public DbSet<Event> Events { get; set; }
        public DbSet<EventImage> EventImages { get; set; }
        public DbSet<EventReport> EventReports { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<Invoice> Invoices { get; set; }

        // Taxonomy extensions
        public DbSet<DriveType> DriveTypes { get; set; }
        public DbSet<CarColor> CarColors { get; set; }

        // Social / stats
        public DbSet<AdvertView> AdvertViews { get; set; }
        public DbSet<UserFollow> UserFollows { get; set; }
        public DbSet<Review> Reviews { get; set; }

        // Notifications
        public DbSet<AppNotification> AppNotifications { get; set; }
        public DbSet<UserNotificationSetting> UserNotificationSettings { get; set; }

        // Event social
        public DbSet<EventAttendee> EventAttendees { get; set; }
        public DbSet<EventFavourite> EventFavourites { get; set; }

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Brand>().HasMany(b => b.Models).WithOne(m => m.Brand).HasForeignKey(m => m.BrandId);
            modelBuilder.Entity<Model>().HasMany(m => m.Generations).WithOne(g => g.Model).HasForeignKey(g => g.ModelId);
            modelBuilder.Entity<Generation>().HasMany(g => g.EngineVersions).WithOne(e => e.Generation).HasForeignKey(e => e.GenerationId);

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
            modelBuilder.Entity<Advert>().ToTable("Adverts").HasKey(a => a.Id);
            modelBuilder.Entity<CarAdvert>().ToTable("CarAdverts");

            modelBuilder.Entity<Advert>(entity =>
            {
                entity.HasOne<User>(a => a.createdBy)
                    .WithMany(u => u.Adverts)
                    .HasForeignKey(a => a.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<CarAdvert>().HasOne(a => a.Brand).WithMany().HasForeignKey(a => a.BrandId);
            modelBuilder.Entity<CarAdvert>().HasOne(a => a.Model).WithMany().HasForeignKey(a => a.ModelId);
            modelBuilder.Entity<CarAdvert>().HasOne(a => a.Generation).WithMany().HasForeignKey(a => a.GenerationId);
            modelBuilder.Entity<CarAdvert>().HasOne(a => a.EngineVersion).WithMany().HasForeignKey(a => a.EngineVersionId);
            modelBuilder.Entity<CarAdvert>().HasOne(a => a.FuelType).WithMany().HasForeignKey(a => a.FuelTypeId);
            modelBuilder.Entity<CarAdvert>().HasOne(a => a.Gearbox).WithMany().HasForeignKey(a => a.GearboxId);
            modelBuilder.Entity<CarAdvert>().HasOne(a => a.BodyType).WithMany().HasForeignKey(a => a.BodyTypeId);
            modelBuilder.Entity<CarAdvert>()
                .HasOne(a => a.VehicleCategory).WithMany().HasForeignKey(a => a.VehicleCategoryId).IsRequired(false);

            modelBuilder.Entity<AdvertImage>().ToTable("AdvertImages").HasKey(i => i.Id);
            modelBuilder.Entity<Advert>().HasMany(a => a.Images).WithOne(i => i.Advert)
                .HasForeignKey(i => i.AdvertId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AdvertFeature>().ToTable("AdvertFeatures").HasKey(af => new { af.AdvertId, af.FeatureId });
            modelBuilder.Entity<AdvertFeature>().HasOne(af => af.Advert).WithMany(a => a.AdvertFeatures)
                .HasForeignKey(af => af.AdvertId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<AdvertFeature>().HasOne(af => af.Feature).WithMany(f => f.AdvertFeatures)
                .HasForeignKey(af => af.FeatureId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<FavoriteAdvert>().ToTable("FavoriteAdverts").HasKey(f => new { f.UserId, f.AdvertId });
            modelBuilder.Entity<FavoriteAdvert>().HasOne(f => f.User).WithMany()
                .HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<FavoriteAdvert>().HasOne(f => f.Advert).WithMany()
                .HasForeignKey(f => f.AdvertId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<VehicleCategory>().ToTable("VehicleCategories").HasKey(c => c.Id);
            
            modelBuilder.Entity<Conversation>().ToTable("Conversations").HasKey(c => c.Id);
            modelBuilder.Entity<Conversation>()
                .HasOne(c => c.Buyer).WithMany()
                .HasForeignKey(c => c.BuyerId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Conversation>()
                .HasOne(c => c.Seller).WithMany()
                .HasForeignKey(c => c.SellerId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Conversation>()
                .HasOne(c => c.Advert).WithMany()
                .HasForeignKey(c => c.AdvertId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Conversation>()
                .HasIndex(c => new { c.BuyerId, c.AdvertId }).IsUnique();

            modelBuilder.Entity<Message>().ToTable("Messages").HasKey(m => m.Id);
            modelBuilder.Entity<Message>()
                .HasOne(m => m.Conversation).WithMany(c => c.Messages)
                .HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Message>()
                .HasOne(m => m.Sender).WithMany()
                .HasForeignKey(m => m.SenderId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Report>().ToTable("Reports").HasKey(r => r.Id);
            modelBuilder.Entity<Report>()
                .HasOne(r => r.ReportedBy).WithMany()
                .HasForeignKey(r => r.ReportedByUserId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Report>()
                .HasOne(r => r.TargetAdvert).WithMany()
                .HasForeignKey(r => r.TargetAdvertId).OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
            modelBuilder.Entity<Report>()
                .HasOne(r => r.TargetUser).WithMany()
                .HasForeignKey(r => r.TargetUserId).OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            modelBuilder.Entity<AdminActionLog>().ToTable("AdminActionLogs").HasKey(l => l.Id);
            modelBuilder.Entity<AdminActionLog>()
                .HasOne(l => l.Admin).WithMany()
                .HasForeignKey(l => l.AdminUserId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Event>().ToTable("Events").HasKey(e => e.Id);
            modelBuilder.Entity<Event>()
                .HasOne(e => e.CreatedBy).WithMany()
                .HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Event>()
                .Property(e => e.Status).HasConversion<string>();

            modelBuilder.Entity<EventImage>().ToTable("EventImages").HasKey(i => i.Id);
            modelBuilder.Entity<EventImage>()
                .HasOne(i => i.Event).WithMany(e => e.Images)
                .HasForeignKey(i => i.EventId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<EventReport>().ToTable("EventReports").HasKey(r => r.Id);
            modelBuilder.Entity<EventReport>()
                .HasOne(r => r.Event).WithMany(e => e.Reports)
                .HasForeignKey(r => r.EventId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<EventReport>()
                .HasOne(r => r.ReportedBy).WithMany()
                .HasForeignKey(r => r.ReportedByUserId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<EventReport>()
                .Property(r => r.Reason).HasConversion<string>();

            modelBuilder.Entity<Payment>().ToTable("Payments").HasKey(p => p.Id);
            modelBuilder.Entity<Payment>()
                .HasOne(p => p.User).WithMany()
                .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Payment>()
                .HasOne(p => p.Advert).WithMany()
                .HasForeignKey(p => p.AdvertId).OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
            modelBuilder.Entity<Payment>()
                .Property(p => p.ServiceType).HasConversion<string>();
            modelBuilder.Entity<Payment>()
                .Property(p => p.Status).HasConversion<string>();
            modelBuilder.Entity<Payment>()
                .Property(p => p.Amount).HasPrecision(10, 2);

            modelBuilder.Entity<Invoice>().ToTable("Invoices").HasKey(i => i.Id);
            modelBuilder.Entity<Invoice>()
                .HasOne(i => i.User).WithMany()
                .HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Invoice>()
                .HasMany(i => i.Payments).WithOne(p => p.Invoice)
                .HasForeignKey(p => p.InvoiceId).OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
            modelBuilder.Entity<Invoice>()
                .Property(i => i.Status).HasConversion<string>();
            modelBuilder.Entity<Invoice>()
                .Property(i => i.TotalAmount).HasPrecision(10, 2);
            modelBuilder.Entity<Invoice>()
                .Property(i => i.NetAmount).HasPrecision(10, 2);
            modelBuilder.Entity<Invoice>()
                .Property(i => i.VatAmount).HasPrecision(10, 2);
            modelBuilder.Entity<Invoice>()
                .Property(i => i.VatRate).HasPrecision(5, 4);

            modelBuilder.Entity<DriveType>().ToTable("DriveTypes").HasKey(d => d.Id);
            modelBuilder.Entity<CarColor>().ToTable("CarColors").HasKey(c => c.Id);
            modelBuilder.Entity<CarAdvert>().HasOne(a => a.DriveType).WithMany().HasForeignKey(a => a.DriveTypeId).IsRequired(false);
            modelBuilder.Entity<CarAdvert>().HasOne(a => a.CarColor).WithMany().HasForeignKey(a => a.ColorId).IsRequired(false);

            modelBuilder.Entity<AdvertView>().ToTable("AdvertViews").HasKey(v => v.Id);
            modelBuilder.Entity<UserFollow>().ToTable("UserFollows").HasKey(f => f.Id);
            modelBuilder.Entity<UserFollow>().HasIndex(f => new { f.FollowerId, f.FollowedId }).IsUnique();
            modelBuilder.Entity<Review>().ToTable("Reviews").HasKey(r => r.Id);

            modelBuilder.Entity<AppNotification>().ToTable("AppNotifications").HasKey(n => n.Id);
            modelBuilder.Entity<AppNotification>().HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<AppNotification>().Property(n => n.Type).HasConversion<string>();
            modelBuilder.Entity<UserNotificationSetting>().ToTable("UserNotificationSettings").HasKey(s => s.Id);
            modelBuilder.Entity<UserNotificationSetting>().HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UserNotificationSetting>().HasIndex(s => new { s.UserId, s.Category }).IsUnique();

            modelBuilder.Entity<EventAttendee>().ToTable("EventAttendees").HasKey(a => a.Id);
            modelBuilder.Entity<EventAttendee>().HasIndex(a => new { a.EventId, a.UserId }).IsUnique();
            modelBuilder.Entity<EventFavourite>().ToTable("EventFavourites").HasKey(f => f.Id);
            modelBuilder.Entity<EventFavourite>().HasIndex(f => new { f.EventId, f.UserId }).IsUnique();

            // Lowercase every table name so EF Core generates lowercase SQL,
            // matching Railway Linux MySQL where tables were imported with
            // lowercase names from Windows (case-insensitive) MySQL.
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var tableName = entityType.GetTableName();
                if (tableName != null)
                    entityType.SetTableName(tableName.ToLower());
            }
        }
    }
}
