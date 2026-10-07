using Microsoft.EntityFrameworkCore;
using SocService.Data;

namespace SocService.Tests;

public static class TestDb
{
    public static SocDbContext Create()
    {
        var options = new DbContextOptionsBuilder<SocDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SocDbContext(options);
    }
}
