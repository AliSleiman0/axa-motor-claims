using Microsoft.EntityFrameworkCore;

namespace Api.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
