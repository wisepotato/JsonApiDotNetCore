using Microsoft.EntityFrameworkCore;

namespace GettingStarted
{
    public class SampleDbContext : DbContext
    {
        public SampleDbContext(DbContextOptions<SampleDbContext> options)
        : base(options)
        { }

        public DbSet<Article> Articles { get; set; }
    }
}