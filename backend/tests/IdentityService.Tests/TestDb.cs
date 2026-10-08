using IdentityService.Data;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Tests;

public static class TestDb
{
    public static IdentityDbContext Create()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new IdentityDbContext(options);
    }
}
