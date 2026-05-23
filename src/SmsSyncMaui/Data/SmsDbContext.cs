using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using SmsSyncMaui.Models;

namespace SmsSyncMaui.Data
{
    public class SmsDbContext : DbContext
    {
        public DbSet<SmsEntry> Messages { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dbPath = Path.Combine(appData, "WinSms", "smssync.db");

            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SmsEntry>()
                .HasIndex(m => m.DateTimestamp);

            modelBuilder.Entity<SmsEntry>()
                .HasIndex(m => m.Address);
        }
    }
}
