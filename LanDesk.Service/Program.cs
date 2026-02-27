using LanDesk.Service.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LanDesk.Service;

class Program
{
    static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "LanDesk Remote Desktop Service";
        });
        builder.Services.AddHostedService<LanDeskBackgroundService>();

        var host = builder.Build();
        host.Run();
    }
}
