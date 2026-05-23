using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using SmsSyncWindows.Models;

namespace SmsSyncWindows.Data
{
    public class SmsDbContext : DbContext
    {
        public DbSet<SmsMessage> Messages { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dbPath = Path.Combine(appData, "WinSms", "smssync.db");
            
            // S'assurer que le dossier existe
            var directory = Path.GetDirectoryName(dbPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SmsMessage>()
                .HasIndex(m => m.DateTimestamp);

            modelBuilder.Entity<SmsMessage>()
                .HasIndex(m => m.Address);
        }
    }
}
