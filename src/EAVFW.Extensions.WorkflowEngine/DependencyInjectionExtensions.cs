
using DotNetDevOps.Extensions.EAVFramework;
using DotNetDevOps.Extensions.EAVFramework.Configuration;
using DotNetDevOps.Extensions.EAVFramework.Endpoints;
using DotNetDevOps.Extensions.EAVFramework.Plugins;
using DotNetDevOps.Extensions.EAVFramework.Shared;
using EAVFW.Extensions.WorkflowEngine;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using WorkflowEngine;
using WorkflowEngine.Core;
using WorkflowEngine.Core.Actions;
using WorkflowEngine.Core.Expressions;

namespace Microsoft.Extensions.DependencyInjection
{

    public static class DependencyInjectionExtensions
    {
       
        public static IEAVFrameworkBuilder AddWorkFlowEngine<TContext, TWorkflowRun>(this IEAVFrameworkBuilder builder)
          where TContext : DynamicContext
          where TWorkflowRun : DynamicEntity, IWorkflowRun, new()
        {
            var services = builder.Services;

            services.AddTransient<IWorkflowExecutor, WorkflowExecutor>();
            services.AddTransient<IActionExecutor, ActionExecutor>();
            services.AddTransient<IHangfireWorkflowExecutor, HangfireWorkflowExecutor>();
            services.AddTransient<IHangfireActionExecutor, HangfireWorkflowExecutor>();
            services.AddSingleton<IWorkflowRepository, DefaultWorkflowRepository>();
            services.AddTransient(typeof(ScheduledWorkflowTrigger<>));

        
            services.AddScoped<IArrayContext, ArrayContext>();
            services.AddScoped<IScopeContext, ScopeContext>();
            services.AddScoped<IRunContextAccessor, RunContextFactory>();
            services.AddAction<ForeachAction>("Foreach");
            services.AddScoped<IOutputsRepository, EAVFWOutputsRepository<TContext, TWorkflowRun>>();

            services.AddFunctions();

            services.AddScoped(typeof(IPluginScheduler<>), typeof(HangFirePluginScheduler<>));
            services.AddTransient<IHangFirePluginSchedulerAsyncContextFactory, DefaultHangFirePluginSchedulerAsyncContextFactory>();
            services.AddHangfire((sp, configuration) => SetupConnection(configuration
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings(), sp.GetRequiredService<IConfiguration>())
                );

            
                services.AddHangfireServer();

            return builder;
        }

        private static void SetupConnection(IGlobalConfiguration globalConfiguration, IConfiguration configuration)
        {
            var options = new SqlServerStorageOptions
            {
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.Zero,
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true,
                SchemaName = configuration.GetValue<string>("DBSchema") ?? "hangfire",

            };

            var a = globalConfiguration.UseSqlServerStorage(configuration.GetValue<string>("ConnectionStrings:ApplicationDB"), options);
            // JobStorage.Current = a.Entry;
        }
    }
}
