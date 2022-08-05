using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Shared
{
    public class TrackerDbContext : DbContext
    {
        public DbSet<Torrent> Torrents { get; set; }
        public DbSet<Peer> Peers { get; set; }
        public TrackerDbContext(DbContextOptions options) : base(options)
        {
            if(Database.EnsureCreated()){
                Database.ExecuteSqlRaw("ALTER TABLE Peers ENGINE=MEMORY;");
            }
            
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.EnableDetailedErrors(false);
            optionsBuilder.EnableSensitiveDataLogging(false);
            
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            
        }


    }
}
