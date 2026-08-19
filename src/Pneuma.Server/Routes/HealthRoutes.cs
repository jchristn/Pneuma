namespace Pneuma.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Pneuma.Core.Observability;
    using Pneuma.Core.Responses;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Health and liveness routes (anonymous).
    /// </summary>
    public class HealthRoutes
    {
        private readonly string _Version;

        /// <summary>Instantiate health routes.</summary>
        /// <param name="version">Service version.</param>
        public HealthRoutes(string version)
        {
            _Version = version ?? "0.1.0";
        }

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/", RootAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Server information", "System"));
            server.Routes.PreAuthentication.Static.Add(HttpMethod.HEAD, "/", RootAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Liveness check", "System"));
            server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/v1.0/api/health", HealthAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Health check", "System"));
            server.Routes.PreAuthentication.Static.Add(HttpMethod.GET, "/metrics", MetricsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Prometheus metrics", "System"));
        }

        private async Task MetricsAsync(HttpContextBase ctx)
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/plain; version=0.0.4";
            await ctx.Response.Send(PneumaMetrics.Render()).ConfigureAwait(false);
        }

        private async Task RootAsync(HttpContextBase ctx)
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/plain";
            await ctx.Response.Send("Pneuma - Pneuma - information brought to life").ConfigureAwait(false);
        }

        private async Task HealthAsync(HttpContextBase ctx)
        {
            HealthResponse health = new HealthResponse { Version = _Version, TimeUtc = DateTime.UtcNow };
            await RouteHelper.SendJsonAsync(ctx, 200, health).ConfigureAwait(false);
        }
    }
}
