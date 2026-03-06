using System;
using Microsoft.EntityFrameworkCore;

namespace CarsWebsite
{
    public class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        
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
        }
    }
}

    