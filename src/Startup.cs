﻿using System;
using System.Threading.Tasks;
using Hamuste.KubernetesAuthentication;
using k8s;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Azure.KeyVault;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Serilog;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace Hamuste
{
    public class Startup {
        
        public Startup(IHostingEnvironment env)
        {
            string appsettingsPath = "appsettings.json";

            if (env.IsDevelopment())
            {
                appsettingsPath = "appsettings.Development.json";
            }

            Console.WriteLine($"Root: {env.ContentRootPath}");

            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile(appsettingsPath, optional: true, reloadOnChange: true)
                .AddJsonFile("secrets/appsettings.secrets.json", optional: true)
                .AddEnvironmentVariables();

            Configuration = builder.Build();
        }


        public IConfiguration Configuration;

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices (IServiceCollection services) {

            services.AddMvc().AddMetrics();

            services.AddSwaggerGen (swagger => {
                swagger.SwaggerDoc ("v1", new Swashbuckle.AspNetCore.Swagger.Info { Title = "Hamuste Swagger" });
            });

            services.AddSingleton<IKubernetes>(s =>
            {
                KubernetesClientConfiguration config;
                config = string.IsNullOrEmpty(Configuration["Kubernetes:ProxyUrl"])
                    ? KubernetesClientConfiguration.InClusterConfig()
                    : new KubernetesClientConfiguration {Host = Configuration["Kubernetes:ProxyUrl"]};


                return new Kubernetes(config);
            });

            services.AddSingleton<IKeyVaultClient>(s =>
            {

                AzureServiceTokenProvider azureServiceTokenProvider = new AzureServiceTokenProvider();
                return new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback));
            });

            services.AddAuthentication().AddScheme<KubernetesAuthenticationOptions, KubernetesAuthenticationHandler>("kubernetes", null);

            services.AddAuthorization(options => {
                options.AddPolicy("KubernetesPolicy", policyBuilder => policyBuilder.RequireAssertion(
                    context => context.Resource as string == context.User.Claims.FirstOrDefault(claim => claim.Type == "sub").Value)
               );
            });

            services.AddSingleton<IConfiguration>(Configuration);

            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        }

        public async Task<string> GetToken(string authority, string resource, string scope)
        {
            Console.WriteLine("Requesting a token!");
            var clientId = Configuration["ActiveDirectory:ClientId"];
            var clientSecret = Configuration["ActiveDirectory:ClientSecret"];
            
            var authContext = new AuthenticationContext(authority);
            ClientCredential clientCred = new ClientCredential(clientId, clientSecret);
            AuthenticationResult result = await authContext.AcquireTokenAsync(resource, clientCred);

            if (result == null)
                throw new InvalidOperationException("Failed to obtain the JWT token");

            return result.AccessToken;
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure (IApplicationBuilder app, IHostingEnvironment env) {

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionMiddleware();
            }

            Log.Logger = new LoggerConfiguration ()
                .ReadFrom.Configuration (Configuration)
                .CreateLogger ();


            app.UseSwagger ();
            app.UseSwaggerUI (c => {
                c.SwaggerEndpoint ("/swagger/v1/swagger.json", "Hamuste Swagger");
            });

            app.UseAuthentication();

            app.UseMvc ();
        }
    }
}