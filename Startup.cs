using System;
using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Polly;
using Polly.Extensions.Http;
using Serilog;
using VSMarketplaceBadges.Formatters;
using VSMarketplaceBadges.Services;
using VSMarketplaceBadges.Middlewares;

namespace VSMarketplaceBadges
{
    public class Startup
    {
        private readonly IWebHostEnvironment environment;

        public Startup(IConfiguration configuration, IWebHostEnvironment env)
        {
            Configuration = configuration;
            this.environment = env;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers()
            .ConfigureApiBehaviorOptions(options => { options.SuppressInferBindingSourcesForParameters = true; });

            services.AddHttpClient<IVSMarketplaceService, VSMarketplaceService>(x =>
            {
                x.BaseAddress = new Uri("https://marketplace.visualstudio.com");
                x.DefaultRequestHeaders.Add("UserAgent", "VSMarketplaceBadges/2.0");
                x.DefaultRequestHeaders.Add("Accept", "application/json;api-version=3.0-preview.1");
            }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
            }).AddPolicyHandler(
            HttpPolicyExtensions.HandleTransientHttpError()
                .OrResult(y => y.StatusCode == HttpStatusCode.NotFound)
                .WaitAndRetryAsync(4, y => TimeSpan.FromSeconds(Math.Pow(3, y))))
                .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromMinutes(5)));

            services.AddHttpClient<IShiledsIoService, ShiledsIoService>(x =>
                {
                    x.BaseAddress = new Uri("https://img.shields.io");
                    x.DefaultRequestHeaders.Add("UserAgent", "VSMarketplaceBadges/2.0");
                }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip
                }).AddPolicyHandler(
                    HttpPolicyExtensions.HandleTransientHttpError()
                        .OrResult(y => y.StatusCode == HttpStatusCode.NotFound)
                        .WaitAndRetryAsync(4, y => TimeSpan.FromSeconds(Math.Pow(3, y))))
                .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromMinutes(5)));
            services.AddResponseCaching();

            services.AddMvc(options =>
            {
                options.OutputFormatters.Insert(0, new ImageOutputFormatter());
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseErrorHandling();
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
            app.UseDefaultFiles();
            app.UseResponseCaching();
            app.UseStaticFiles();

            //app.UseSerilogRequestLogging();
        }
    }
}
