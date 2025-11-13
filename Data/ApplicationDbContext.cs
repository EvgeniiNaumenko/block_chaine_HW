using BlockChain_FP_ITStep.Models;
using System.Collections.Generic;
using System.Reflection.Emit;
using Microsoft.EntityFrameworkCore;

namespace BlockChain_FP_ITStep.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Block> Blocks { get; set; } = null!;
        public DbSet<Transaction> Transactions { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);


            modelBuilder.Entity<Block>(entity =>
            {
                entity.HasIndex(b => b.Index);

                entity.HasIndex(b => new { b.NodeId, b.Index })
                    .IsUnique();

                entity.HasMany(b => b.Transactions)
                    .WithOne(t => t.Block)
                    .HasForeignKey(t => t.BlockId)
                    .OnDelete(DeleteBehavior.Restrict);

            });



        }
    }
}
