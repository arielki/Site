﻿using GeoAPI.Geometries;
using IsraelHiking.API;
using IsraelHiking.API.Controllers;
using IsraelHiking.API.Swagger;
using IsraelHiking.Common;
using IsraelHiking.DataAccess;
using IsraelHiking.DataAccess.Database;
using IsraelHiking.DataAccessInterfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Converters;
using NLog.Extensions.Logging;
using NLog.Web;
using Swashbuckle.AspNetCore.Swagger;
using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;

namespace IsraelHiking.Web
{
    // HM TODO: workaround until issue is resolved:
    // https://github.com/aspnet/BasicMiddleware/issues/194

    public class RewriteWithQueryRule : IRule
    {
        private readonly TimeSpan _regexTimeout = TimeSpan.FromSeconds(1);
        public Regex InitialMatch { get; }
        public string Replacement { get; }
        public bool StopProcessing { get; }
        public RewriteWithQueryRule(string regex, string replacement, bool stopProcessing)
        {
            if (string.IsNullOrEmpty(regex))
            {
                throw new ArgumentException(nameof(regex));
            }

            if (string.IsNullOrEmpty(replacement))
            {
                throw new ArgumentException(nameof(replacement));
            }

            InitialMatch = new Regex(regex, RegexOptions.Compiled | RegexOptions.CultureInvariant, _regexTimeout);
            Replacement = replacement;
            StopProcessing = stopProcessing;
        }

        public virtual void ApplyRule(RewriteContext context)
        {
            var pathWithQuery = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString;
            var initMatchResults = InitialMatch.Match(pathWithQuery == PathString.Empty 
                ? pathWithQuery 
                : pathWithQuery.Substring(1));
            if (!initMatchResults.Success)
            {
                return;
            }
            var result = initMatchResults.Result(Replacement);
            var request = context.HttpContext.Request;

            if (StopProcessing)
            {
                context.Result = RuleResult.SkipRemainingRules;
            }

            if (string.IsNullOrEmpty(result))
            {
                result = "/";
            }

            if (result.IndexOf("://", StringComparison.Ordinal) >= 0)
            {
                UriHelper.FromAbsolute(result, out var scheme, out var host, out var pathString, out var query, out FragmentString _);

                request.Scheme = scheme;
                request.Host = host;
                request.Path = pathString;
                request.QueryString = query.Add(request.QueryString);
            }
            else
            {
                var split = result.IndexOf('?');
                if (split >= 0)
                {
                    var newPath = result.Substring(0, split);
                    request.Path = newPath[0] == '/' 
                        ? PathString.FromUriComponent(newPath) 
                        : PathString.FromUriComponent('/' + newPath);
                    request.QueryString = request.QueryString.Add(QueryString.FromUriComponent(result.Substring(split)));
                }
                else
                {
                    request.Path = result[0] == '/' 
                        ? PathString.FromUriComponent(result) 
                        : PathString.FromUriComponent('/' + result);
                }
            }
        }
    }

    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc(options =>
            {
                options.ModelMetadataDetailsProviders.Add(new SuppressChildValidationMetadataProvider(typeof(Feature)));
                options.ModelMetadataDetailsProviders.Add(new SuppressChildValidationMetadataProvider(typeof(PointOfInterestExtended)));
            }).AddJsonOptions(options =>
            {
                options.SerializerSettings.Converters.Add(new CoordinateConverter());
                options.SerializerSettings.Converters.Add(new GeometryConverter());
                options.SerializerSettings.Converters.Add(new FeatureCollectionConverter());
                options.SerializerSettings.Converters.Add(new FeatureConverter());
                options.SerializerSettings.Converters.Add(new AttributesTableConverter());
                options.SerializerSettings.Converters.Add(new ICRSObjectConverter());
                options.SerializerSettings.Converters.Add(new GeometryArrayConverter());
                options.SerializerSettings.Converters.Add(new EnvelopeConverter());
            });
            services.AddCors();
            services.AddOptions();
            
            var configFileName = services.BuildServiceProvider().GetService<IHostingEnvironment>().IsDevelopment()
                ? "appsettings.json"
                : "appsettings.Production.json";
            var config = new ConfigurationBuilder()
                .AddJsonFile(configFileName, optional: false, reloadOnChange: true)
                .Build();
            services.Configure<ConfigurationData>(config);
            var nonPublicConfigurationFilePath = services.BuildServiceProvider().GetService<IOptions<ConfigurationData>>().Value.NonPublicConfigurationFilePath;
            var nonPublicConfiguration = new ConfigurationBuilder()
                .AddJsonFile(nonPublicConfigurationFilePath, optional: true, reloadOnChange: true)
                .Build();
            services.Configure<NonPublicConfigurationData>(nonPublicConfiguration);

            services.AddSingleton(serviceProvider => serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("IHM"));
            var binariesFolder = "";
            services.AddTransient<IFileProvider, PhysicalFileProvider>((serviceProvider) =>
            {
                binariesFolder = GetBinariesFolder(serviceProvider);
                return new PhysicalFileProvider(binariesFolder);
            });

            services.AddIHMDataAccess();
            services.AddIHMApi();

            services.AddSingleton<IGeometryFactory, GeometryFactory>((serviceProvider) => new GeometryFactory(new PrecisionModel(100000000)));
            services.AddSingleton<ISecurityTokenValidator, OsmAccessTokenValidator>();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new Info { Title = "Israel Hiking API", Version = "v1" });
                c.SchemaFilter<FeatureExampleFilter>();
                c.SchemaFilter<FeatureCollectionExampleFilter>();
                c.OperationFilter<AssignOAuthSecurityRequirements>();
                c.IncludeXmlComments(Path.Combine(binariesFolder, "israelhiking.API.xml"));
            });
            services.AddEntityFrameworkSqlite().AddDbContext<IsraelHikingDbContext>();
            services.AddDirectoryBrowser();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            env.ConfigureNLog("IsraelHiking.Web.nlog");
            loggerFactory.AddNLog();

            var rewriteOptions = new RewriteOptions();
            rewriteOptions.Rules.Add(new RewriteWithQueryRule(".*_escaped_fragment_=%2F%3Fs%3D(.*)", "api/opengraph/$1", false));

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                rewriteOptions.AddRedirectToHttps();
            }
            app.UseRewriter(rewriteOptions);

            app.UseCors(builder =>
            {
                builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader().AllowCredentials();
            });

            var jwtBearerOptions = new JwtBearerOptions();
            jwtBearerOptions.SecurityTokenValidators.Clear();
            jwtBearerOptions.SecurityTokenValidators.Add(app.ApplicationServices.GetRequiredService<ISecurityTokenValidator>());
            app.UseJwtBearerAuthentication(jwtBearerOptions);

            app.UseMvc();
            SetupStaticFiles(app);
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Israel Hiking API V1");
            });

            app.Run(context =>
            {
                context.Response.StatusCode = 404;
                context.Response.ContentType = "text/html";
                var file = env.WebRootFileProvider.GetFileInfo("/resource-not-found.html");
                context.Response.ContentLength = file.Length;
                return context.Response.SendFileAsync(file);
            });
            InitializeServices(app.ApplicationServices);
        }

        private static void SetupStaticFiles(IApplicationBuilder app)
        {
            app.UseDefaultFiles();
            var configurationData = app.ApplicationServices.GetRequiredService<IOptions<ConfigurationData>>().Value;
            var fileExtensionContentTypeProvider = new FileExtensionContentTypeProvider();
            fileExtensionContentTypeProvider.Mappings.Add(".db", "application/octet-stream");
            foreach (var directory in configurationData.ListingDictionary)
            {
                var fileServerOptions = new FileServerOptions
                {
                    FileProvider = new PhysicalFileProvider(directory.Value),
                    RequestPath = new PathString("/" + directory.Key),
                    EnableDirectoryBrowsing = true,
                    DirectoryBrowserOptions =
                    {
                        FileProvider = new PhysicalFileProvider(directory.Value),
                        RequestPath = new PathString("/" + directory.Key),
                        Formatter = new BootstrapFontAwesomeDirectoryFormatter(app.ApplicationServices
                            .GetRequiredService<IFileSystemHelper>())
                    },
                    StaticFileOptions = {ContentTypeProvider = fileExtensionContentTypeProvider},
                };
                app.UseFileServer(fileServerOptions);
            }
            // serve https certificate folder
            var wellKnownFolder = Path.Combine(Directory.GetCurrentDirectory(), @".well-known");
            if (Directory.Exists(wellKnownFolder))
            {
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(wellKnownFolder),
                    RequestPath = new PathString("/.well-known"),
                    ServeUnknownFileTypes = true // serve extensionless file
                });
            }
            // wwwroot
            app.UseStaticFiles();
        }

        private void InitializeServices(IServiceProvider serviceProvider)
        {
            var logger = serviceProvider.GetRequiredService<ILogger>();
            logger.LogInformation("Initializing Elevation data and Elastic Search Service");
            serviceProvider.GetRequiredService<IElasticSearchGateway>().Initialize();
            serviceProvider.GetRequiredService<IElevationDataStorage>().Initialize().ContinueWith(task => logger.LogInformation("Finished loading elevation data from files."));
            serviceProvider.GetRequiredService<IWikipediaGateway>().Initialize().ContinueWith(task => logger.LogInformation("Finished loading wikipedia gateway."));
        }

        private string GetBinariesFolder(IServiceProvider serviceProvider)
        {
            var binariesFolder = serviceProvider.GetService<IOptions<ConfigurationData>>().Value.BinariesFolder;
            return Path.Combine(Directory.GetCurrentDirectory(), binariesFolder);
        }
    }
}
