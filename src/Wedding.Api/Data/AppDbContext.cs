using Microsoft.EntityFrameworkCore;

namespace Wedding.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Guest> Guests => Set<Guest>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<GuestEvent> GuestEvents => Set<GuestEvent>();
    public DbSet<Companion> Companions => Set<Companion>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<Answer> Answers => Set<Answer>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<Upload> Uploads => Set<Upload>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GuestEvent>().HasKey(ge => new { ge.GuestId, ge.EventId });

        modelBuilder.Entity<Answer>()
            .HasIndex(a => new { a.GuestId, a.CompanionId, a.QuestionId })
            .IsUnique();

        modelBuilder.Entity<Setting>().HasKey(s => s.Key);

        modelBuilder.Entity<Submission>()
            .HasIndex(s => s.GuestId);
    }
}