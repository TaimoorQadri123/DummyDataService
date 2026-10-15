using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DummyDataWorker.Models;
using Microsoft.EntityFrameworkCore;
using DummyDataWorker.Models;
using Microsoft.EntityFrameworkCore;

namespace DummyDataWorker.Data
{
    public class WorkerDbContext : DbContext
    {
        public WorkerDbContext(DbContextOptions<WorkerDbContext> options)
            : base(options)
        {
        }

        public DbSet<WorkerDummyData> WorkerDummyData { get; set; }
        public DbSet<SourceTransaction> SourceTransactions { get; set; }
        public DbSet<LocalTransaction> LocalTransactions { get; set; }
        public DbSet<ModifyPoint> ModifyPoints { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SourceTransaction>(e =>
            {
                e.ToTable("SourceTransaction");
                e.HasKey(x => x.TranId);
                e.Property(x => x.TranId).ValueGeneratedOnAdd();
                e.Property(x => x.Amount).HasPrecision(18, 2);
            });

            modelBuilder.Entity<LocalTransaction>(e =>
            {
                e.ToTable("LocalTransaction");
                e.HasKey(x => x.TranId);
                e.Property(x => x.TranId).ValueGeneratedNever();
                e.Property(x => x.Amount).HasPrecision(18, 2);
            });

            modelBuilder.Entity<ModifyPoint>().ToTable("ModifyPoint");

            modelBuilder.Entity<WorkerDummyData>()
                .Property(x => x.Amount).HasPrecision(18, 2);
        }
    }
}