using System.Text.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers.Http;

namespace ZSlayerZombies;

[Injectable(TypePriority = 0)]
public class ZombieHttpListener(
    ZSlayerZombiesMod zombiesMod,
    ZombieService zombieService,
    ISptLogger<ZombieHttpListener> logger) : IHttpListener
{
    private const string BasePath = "/zslayer/zombies";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public bool CanHandle(MongoId sessionId, HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        return path.Equals(BasePath, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(BasePath + "/", StringComparison.OrdinalIgnoreCase);
    }

    public async Task Handle(MongoId sessionId, HttpContext context)
    {
        // CORS headers
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-Session-Id";

        if (context.Request.Method == "OPTIONS")
        {
            context.Response.StatusCode = 200;
            await context.Response.StartAsync();
            await context.Response.CompleteAsync();
            return;
        }

        var rawPath = context.Request.Path.Value ?? "";
        var path = rawPath.Length > BasePath.Length
            ? rawPath.Substring(BasePath.Length).TrimStart('/').TrimEnd('/')
            : "";
        var method = context.Request.Method;

        try
        {
            switch (path)
            {
                case "config" when method == "GET":
                    await RespondJson(context, zombiesMod.GetConfig());
                    break;

                case "config" when method == "POST":
                    var body = await ReadBody(context);
                    var newConfig = JsonSerializer.Deserialize<ZombieConfig>(body, JsonOptions);
                    if (newConfig == null)
                    {
                        await RespondError(context, 400, "Invalid config JSON");
                        return;
                    }
                    zombiesMod.UpdateConfig(newConfig);
                    await RespondJson(context, new { success = true, message = "Config updated and applied" });
                    break;

                case "status" when method == "GET":
                    var status = zombieService.GetStatus(zombiesMod.GetConfig());
                    await RespondJson(context, status);
                    break;

                case "apply" when method == "POST":
                    zombiesMod.ReApply();
                    await RespondJson(context, new { success = true, message = "Config re-applied" });
                    break;

                case "reset" when method == "POST":
                    zombiesMod.ResetToDefaults();
                    await RespondJson(context, new { success = true, message = "Reset to defaults" });
                    break;

                default:
                    await RespondError(context, 404, $"Unknown endpoint: {path}");
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.Error($"[ZSlayerZombies] HTTP error on {method} {path}: {ex.Message}");
            await RespondError(context, 500, ex.Message);
        }
    }

    private static async Task RespondJson(HttpContext context, object data)
    {
        context.Response.StatusCode = 200;
        context.Response.ContentType = "application/json";
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await context.Response.WriteAsync(json);
    }

    private static async Task RespondError(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        var json = JsonSerializer.Serialize(new { error = message }, new JsonSerializerOptions());
        await context.Response.WriteAsync(json);
    }

    private static async Task<string> ReadBody(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body);
        return await reader.ReadToEndAsync();
    }
}
