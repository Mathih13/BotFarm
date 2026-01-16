using System;
using System.Threading.Tasks;
using BotFarm.Testing;
using BotFarm.Web.Hubs;
using BotFarm.Web.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BotFarm.Web
{
    /// <summary>
    /// Hosts the embedded ASP.NET Core web API with SignalR
    /// </summary>
    internal class WebApiHost : IAsyncDisposable
    {
        private WebApplication app;
        private readonly BotFactory factory;
        private readonly int port;

        public WebApiHost(BotFactory factory, TestRunCoordinator testCoordinator, TestSuiteCoordinator suiteCoordinator, int port = 5000)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.port = port;

            var builder = WebApplication.CreateBuilder();

            // Configure Kestrel to listen on the specified port
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(port);
            });

            // Add services
            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                    options.JsonSerializerOptions.WriteIndented = true;
                });

            builder.Services.AddSignalR();

            // CORS for development (Vite dev server on localhost:5173)
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                    policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
                          .AllowAnyMethod()
                          .AllowAnyHeader()
                          .AllowCredentials()); // Required for SignalR
            });

            // Register singletons
            builder.Services.AddSingleton(factory);
            builder.Services.AddSingleton(factory.Logs);
            builder.Services.AddSingleton(testCoordinator);
            builder.Services.AddSingleton(suiteCoordinator);
            builder.Services.AddSingleton<TestEventBroadcaster>();

            app = builder.Build();

            // Configure middleware pipeline
            app.UseCors();

            // Map API controllers
            app.MapControllers();

            // Map SignalR hub
            app.MapHub<TestHub>("/hubs/test");

            // In production, serve static files for the frontend
            // The frontend build should be copied to wwwroot/
            var wwwrootPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
            if (System.IO.Directory.Exists(wwwrootPath))
            {
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(wwwrootPath)
                });

                // SPA fallback for client-side routing
                app.MapFallbackToFile("index.html");
            }
        }

        public async Task StartAsync()
        {
            await app.StartAsync();
            factory.Log($"Web API started on http://localhost:{port}");

            // Initialize the event broadcaster after the app starts
            var broadcaster = app.Services.GetRequiredService<TestEventBroadcaster>();
            broadcaster.Initialize();
        }

        public async Task StopAsync()
        {
            if (app != null)
            {
                await app.StopAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (app != null)
            {
                await app.DisposeAsync();
                app = null;
            }
        }
    }
}
