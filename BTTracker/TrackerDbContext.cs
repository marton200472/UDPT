using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTTracker
{
    internal class TrackerDbContext : DbContext
    {
        //public DbSet<Peer> Peers { get; set; }
        public TrackerDbContext(DbContextOptions options):base(options)
        {
            Database.EnsureCreated();
            
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            
            if (!optionsBuilder.IsConfigured)
            {

            }
        }

    }
}
