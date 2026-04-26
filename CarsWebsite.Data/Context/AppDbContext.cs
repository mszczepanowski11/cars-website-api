using System;
using System.Text.Json;
using cars_website_api.CarsWebsite.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CarsWebsite
{
    public class AppDbContext : DbContext
        
    {
        public DbSet<User> Users { get; set; }
        
        public DbSet<Advert> Adverts { get; set; }
        public DbSet<CarAdvert> CarAdverts { get; set; }
        
        
        public DbSet<AdvertImage> AdvertImages { get; set; }
        public DbSet<AdvertFeature> AdvertFeatures { get; set; }
        
        
        public DbSet<Brand> Brands { get; set; }
        public DbSet<Model> Models { get; set; }
        public DbSet<Generation> Generations { get; set; }
        public DbSet<EngineVersion> EngineVersions { get; set; }
        public DbSet<FuelType> FuelTypes { get; set; }
        public DbSet<Gearbox> Gearboxes { get; set; }
        public DbSet<BodyType> BodyTypes { get; set; }
        public DbSet<Feature> Features { get; set; }
        public DbSet<FeatureCategory> FeatureCategories { get; set; }
        
        
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }
        
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseMySql("DefaultConnection",
                    new MySqlServerVersion(new Version(8, 0, 21)));
            } 
        }
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            
            modelBuilder.Entity<Brand>()
                .HasMany(b => b.Models)
                .WithOne(m => m.Brand)
                .HasForeignKey(m => m.BrandId);

            modelBuilder.Entity<Model>()
                .HasMany(m => m.Generations)
                .WithOne(g => g.Model)
                .HasForeignKey(g => g.ModelId);

            modelBuilder.Entity<Generation>()
                .HasMany(g => g.EngineVersions)
                .WithOne(e => e.Generation)
                .HasForeignKey(e => e.GenerationId);
            
            modelBuilder.Entity<User>()
                .HasKey(user => user.Id);
            
            modelBuilder.Entity<Advert>()
                .ToTable("Adverts")
                .HasKey(a => a.Id);

            modelBuilder.Entity<CarAdvert>()
                .ToTable("CarAdverts");

            modelBuilder.Entity<Advert>(entity =>
            {
                entity.HasOne<User>(a => a.createdBy)
                    .WithMany(u => u.Adverts)
                    .HasForeignKey(a => a.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
            
            
            
            /////////////////////////////////////////CAR
            modelBuilder.Entity<CarAdvert>()
                .HasOne(a => a.Brand)
                .WithMany()
                .HasForeignKey(a => a.BrandId);

            modelBuilder.Entity<CarAdvert>()
                .HasOne(a => a.Model)
                .WithMany()
                .HasForeignKey(a => a.ModelId);

            modelBuilder.Entity<CarAdvert>()
                .HasOne(a => a.Generation)
                .WithMany()
                .HasForeignKey(a => a.GenerationId);

            modelBuilder.Entity<CarAdvert>()
                .HasOne(a => a.EngineVersion)
                .WithMany()
                .HasForeignKey(a => a.EngineVersionId);

            modelBuilder.Entity<CarAdvert>()
                .HasOne(a => a.FuelType)
                .WithMany()
                .HasForeignKey(a => a.FuelTypeId);

            modelBuilder.Entity<CarAdvert>()
                .HasOne(a => a.Gearbox)
                .WithMany()
                .HasForeignKey(a => a.GearboxId);

            modelBuilder.Entity<CarAdvert>()
                .HasOne(a => a.BodyType)
                .WithMany()
                .HasForeignKey(a => a.BodyTypeId);
            
            
            ///////////////////////////////////IMAGES
            modelBuilder.Entity<AdvertImage>()
                .ToTable("AdvertImages")
                .HasKey(i => i.Id);

            modelBuilder.Entity<Advert>()
                .HasMany(a => a.Images)
                .WithOne(i => i.Advert)
                .HasForeignKey(i => i.AdvertId)
                .OnDelete(DeleteBehavior.Cascade);
            
            
            //////////////////////////////////FEATURES
            modelBuilder.Entity<AdvertFeature>()
                .ToTable("AdvertFeatures")
                .HasKey(af => new { af.AdvertId, af.FeatureId });

            modelBuilder.Entity<AdvertFeature>()
                .HasOne(af => af.Advert)
                .WithMany(a => a.AdvertFeatures)
                .HasForeignKey(af => af.AdvertId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AdvertFeature>()
                .HasOne(af => af.Feature)
                .WithMany(f => f.AdvertFeatures)
                .HasForeignKey(af => af.FeatureId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}

    