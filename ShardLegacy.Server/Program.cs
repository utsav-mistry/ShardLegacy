using ShardLegacy.Server.Services;

namespace ShardLegacy.Server
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddSingleton<MongoDbService>();
            builder.Services.AddSingleton<DeploymentService>();

            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                });

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            // Initialize persistence (optional MongoDB)
            try
            {
                var mongo = app.Services.GetRequiredService<MongoDbService>();
                await mongo.EnsureIndexesAsync();
                var pingOk = await mongo.PingAsync();

                var logger = app.Services.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("MongoDB ping: {Ok}", pingOk);

                if (pingOk)
                {
                    var deploymentSvc = app.Services.GetRequiredService<DeploymentService>();
                    await deploymentSvc.InitializeAsync();
                }
            }
            catch (Exception ex)
            {
                var logger = app.Services.GetRequiredService<ILogger<Program>>();
                logger.LogWarning(ex, "MongoDB initialization failed. Continuing without persistence.");
            }

            app.UseDefaultFiles();
            app.MapStaticAssets();

            // Enable WebSockets for live log streaming
            app.UseWebSockets();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();
            app.UseAuthorization();

            app.MapControllers();
            app.MapFallbackToFile("/index.html");

            app.Run();
        }
    }
}