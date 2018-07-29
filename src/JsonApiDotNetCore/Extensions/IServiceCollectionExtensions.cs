using System;
using System.Collections.Generic;
using JsonApiDotNetCore.Builders;
using JsonApiDotNetCore.Configuration;
using JsonApiDotNetCore.Data;
using JsonApiDotNetCore.Formatters;
using JsonApiDotNetCore.Internal;
using JsonApiDotNetCore.Internal.Generics;
using JsonApiDotNetCore.Middleware;
using JsonApiDotNetCore.Serialization;
using JsonApiDotNetCore.Services;
using JsonApiDotNetCore.Services.Operations;
using JsonApiDotNetCore.Services.Operations.Processors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JsonApiDotNetCore.Extensions
{
    // ReSharper disable once InconsistentNaming
    public static class IServiceCollectionExtensions
    {
        internal static List<ServiceDescriptor> RequiredServices = new List<ServiceDescriptor>
        {
            new ServiceDescriptor(typeof(IEntityRepository<>), typeof(DefaultEntityRepository<>), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IEntityRepository<,>), typeof(DefaultEntityRepository<,>), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(ICreateService<>), typeof(EntityResourceService<>), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(ICreateService<,>), typeof(EntityResourceService<,>), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IGetAllService<>), typeof(EntityResourceService<>), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IGetAllService<,>), typeof(EntityResourceService<,>), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IGetByIdService<>), typeof(EntityResourceService<>), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IGetByIdService<,>), typeof(EntityResourceService<,>), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IGetRelationshipService<,>), typeof(EntityResourceService<>), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IGetRelationshipService<,>), typeof(EntityResourceService<,>), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IUpdateService<>), typeof(EntityResourceService<>), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IUpdateService<,>), typeof(EntityResourceService<,>), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IDeleteService<>), typeof(EntityResourceService<>), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IDeleteService<,>), typeof(EntityResourceService<,>), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IResourceService<>), typeof(EntityResourceService<>), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IResourceService<,>), typeof(EntityResourceService<,>), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IJsonApiContext), typeof(JsonApiContext), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IScopedServiceProvider), typeof(RequestScopedServiceProvider), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(JsonApiRouteHandler), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IMetaBuilder), typeof(MetaBuilder), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IDocumentBuilder), typeof(DocumentBuilder), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IJsonApiSerializer), typeof(JsonApiSerializer), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IJsonApiWriter), typeof(JsonApiWriter), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IJsonApiDeSerializer), typeof(JsonApiDeSerializer), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IJsonApiReader), typeof(JsonApiReader), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IGenericProcessorFactory), typeof(GenericProcessorFactory), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(GenericProcessor<>), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IQueryAccessor), typeof(QueryAccessor), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IQueryParser), typeof(QueryParser), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IControllerContext), typeof(Services.ControllerContext), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IDocumentBuilderOptionsProvider), typeof(DocumentBuilderOptionsProvider), ServiceLifetime.Scoped),
            new ServiceDescriptor(typeof(IHttpContextAccessor), typeof(HttpContextAccessor), ServiceLifetime.Singleton),
        };

        public static IServiceCollection AddJsonApi<TContext>(this IServiceCollection services)
            where TContext : DbContext
        {
            var mvcBuilder = services.AddMvcCore();
            return AddJsonApi<TContext>(services, opt => { }, mvcBuilder);
        }

        public static IServiceCollection AddJsonApi<TContext>(this IServiceCollection services, Action<JsonApiOptions> options)
            where TContext : DbContext
        {
            var mvcBuilder = services.AddMvcCore();
            return AddJsonApi<TContext>(services, options, mvcBuilder);
        }

        public static IServiceCollection AddJsonApi<TContext>(this IServiceCollection services,
           Action<JsonApiOptions> options,
           IMvcCoreBuilder mvcBuilder) where TContext : DbContext
        {
            var config = new JsonApiOptions();

            options(config);

            config.BuildContextGraph(builder => builder.AddDbContext<TContext>());

            mvcBuilder
                .AddMvcOptions(opt =>
                {
                    opt.Filters.Add(typeof(JsonApiExceptionFilter));
                    opt.SerializeAsJsonApi(config);
                });

            AddJsonApiInternals<TContext>(services, config);
            return services;
        }

        public static IServiceCollection AddJsonApi(this IServiceCollection services,
            Action<JsonApiOptions> options,
            IMvcCoreBuilder mvcBuilder)
        {
            var config = new JsonApiOptions();

            options(config);

            mvcBuilder
                .AddMvcOptions(opt =>
                {
                    opt.Filters.Add(typeof(JsonApiExceptionFilter));
                    opt.SerializeAsJsonApi(config);
                });

            AddJsonApiInternals(services, config);
            return services;
        }

        public static void AddJsonApiInternals<TContext>(
            this IServiceCollection services,
            JsonApiOptions jsonApiOptions) where TContext : DbContext
        {
            if (jsonApiOptions.ContextGraph == null)
                jsonApiOptions.BuildContextGraph<TContext>(null);

            services.AddScoped<IDbContextResolver, DbContextResolver<TContext>>();

            AddJsonApiInternals(services, jsonApiOptions);
        }

        public static void AddJsonApiInternals(
            this IServiceCollection services,
            JsonApiOptions jsonApiOptions)
        {
            if (jsonApiOptions.ContextGraph.UsesDbContext == false)
            {
                services.AddScoped<DbContext>();
                services.AddSingleton(new DbContextOptionsBuilder().Options);
            }

            if (jsonApiOptions.EnableOperations)
                AddOperationServices(services);

            services.AddSingleton(jsonApiOptions);
            services.AddSingleton(jsonApiOptions.ContextGraph);

            foreach (var svc in RequiredServices)
                services.Add(svc);
        }

        private static void AddOperationServices(IServiceCollection services)
        {
            services.AddScoped<IOperationsProcessor, OperationsProcessor>();

            services.AddScoped(typeof(ICreateOpProcessor<>), typeof(CreateOpProcessor<>));
            services.AddScoped(typeof(ICreateOpProcessor<,>), typeof(CreateOpProcessor<,>));

            services.AddScoped(typeof(IGetOpProcessor<>), typeof(GetOpProcessor<>));
            services.AddScoped(typeof(IGetOpProcessor<,>), typeof(GetOpProcessor<,>));

            services.AddScoped(typeof(IRemoveOpProcessor<>), typeof(RemoveOpProcessor<>));
            services.AddScoped(typeof(IRemoveOpProcessor<,>), typeof(RemoveOpProcessor<,>));

            services.AddScoped(typeof(IUpdateOpProcessor<>), typeof(UpdateOpProcessor<>));
            services.AddScoped(typeof(IUpdateOpProcessor<,>), typeof(UpdateOpProcessor<,>));

            services.AddScoped<IOperationProcessorResolver, OperationProcessorResolver>();
        }

        public static void SerializeAsJsonApi(this MvcOptions options, JsonApiOptions jsonApiOptions)
        {
            options.InputFormatters.Insert(0, new JsonApiInputFormatter());

            options.OutputFormatters.Insert(0, new JsonApiOutputFormatter());

            options.Conventions.Insert(0, new DasherizedRoutingConvention(jsonApiOptions.Namespace));
        }
    }
}
