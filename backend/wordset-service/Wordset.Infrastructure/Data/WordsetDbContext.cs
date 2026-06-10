using Microsoft.EntityFrameworkCore;
using Wordset.Domain.Entities;

namespace Wordset.Infrastructure.Data;

public class WordsetDbContext(DbContextOptions<WordsetDbContext> options) : DbContext(options)
{
    public DbSet<Domain.Entities.Wordset> Wordsets => Set<Domain.Entities.Wordset>();
    public DbSet<Word> Words => Set<Word>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Domain.Entities.Wordset>(entity =>
        {
            entity.ToTable("wordsets");
            entity.HasKey(w => w.Id);
            entity.Property(w => w.Id).HasColumnName("id");
            entity.Property(w => w.Name).HasColumnName("name").IsRequired();
            entity.Property(w => w.ShareCode).HasColumnName("share_code").HasMaxLength(6).IsRequired();
            entity.Property(w => w.CreatedAt).HasColumnName("created_at");
            entity.Property(w => w.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(w => w.ShareCode).IsUnique();
        });

        modelBuilder.Entity<Word>(entity =>
        {
            entity.ToTable("words");
            entity.HasKey(w => new { w.WordsetId, w.Value });
            entity.Property(w => w.WordsetId).HasColumnName("wordset_id");
            entity.Property(w => w.Value).HasColumnName("word");
            entity.Property(w => w.FrequencyRank).HasColumnName("frequency_rank");
        });
    }
}
