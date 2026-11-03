using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FlowerShop.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FlowerShopDbContext>
{
    public FlowerShopDbContext CreateDbContext(string[] args)
    {
        // Chỉ phục vụ sinh migration; thông tin kết nối thực tế được cấp từ môi trường.
        var options = new DbContextOptionsBuilder<FlowerShopDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("FLOWERSHOP_CONNECTION_STRING") ?? "Host=localhost;Database=flowershop_design;Username=postgres").Options;
        return new FlowerShopDbContext(options);
    }
}
