using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;


namespace CarsWebsite
{
    public class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<Advert> Adverts { get; set; }
        
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
            modelBuilder.Entity<User>()
                .HasKey(user => user.Id);

            modelBuilder.Entity<Advert>(entity =>
            {
                entity.Property(a => a.AdvertType)
                    .HasConversion<string>();

                entity.Property(a => a.Images)
                    .HasConversion(
                        v => JsonSerializer.Serialize(v, (JsonSerializerOptions)null),
                        v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions)null)
                    );

                entity.HasOne<User>(a => a.createdBy)
                    .WithMany(u => u.Adverts)
                    .HasForeignKey(a => a.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.OwnsOne(a => a.VehicleDetails, v =>
                {
                    v.Property(x => x.VehicleType).HasConversion<string>();
                    v.Property(x => x.FuelType).HasConversion<string>();
                    v.Property(x => x.Transmission).HasConversion<string>();
                    v.Property(x => x.Condition).HasConversion<string>();
                });


            });
        }
    }
}

    