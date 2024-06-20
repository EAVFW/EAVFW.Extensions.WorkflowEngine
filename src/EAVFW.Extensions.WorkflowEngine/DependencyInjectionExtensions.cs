
using EAVFramework;
using EAVFramework.Configuration;
using EAVFramework.Endpoints;
using EAVFramework.Endpoints.Results;
using EAVFramework.Extensions;
using EAVFramework.Plugins;
using EAVFramework.Shared;
using EAVFW.Extensions.WorkflowEngine;
using ExpressionEngine;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using WorkflowEngine;
using WorkflowEngine.Core;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;
using Newtonsoft.Json.Serialization;
using System.Net.Http;
using EAVFW.Extensions.WorkflowEngine.Models;
using System.Xml.Linq;
using EAVFW.Extensions.WorkflowEngine.Endpoints;
using EAVFW.Extensions.WorkflowEngine.Abstractions;
using EAVFW.Extensions.Configuration.Forms;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class EAVWorkflowExtensions
    {
        public static ClaimsPrincipal GetRunningPrincipal(this IRunContext context)
        {
            return new ClaimsPrincipal(new ClaimsIdentity(new Claim[] {
                                   new Claim("sub",context.PrincipalId)
                                }, EAVFramework.Constants.DefaultCookieAuthenticationScheme));
        }
    }
    public static class DependencyInjectionExtensions
    {
        public static IEndpointRouteBuilder MapWorkFlowEndpoints<TContext, TWorkflowRun>(
            this IEndpointRouteBuilder endpoints)
            where TContext : DynamicContext
            where TWorkflowRun : DynamicEntity, IWorkflowRun, new()
        {
            var options = endpoints.ServiceProvider.GetRequiredService<IOptions<WorkflowEndpointOptions>>();
            
            if (options.Value.IncludeListWorkflows)
            {
                endpoints.MapGet("/api/workflows", async x =>
                {
                    var workflows = x.RequestServices.GetService<IEnumerable<IWorkflow>>()
                    .Select(x => new { name = x.GetType().Name, id = x.Id })
                    .ToArray();
                    await x.Response.WriteJsonAsync(new { value = workflows });
                });
            }

            if (options.Value.IncludeWorkflowMetadata)
            {
                endpoints.MapGet("/api/workflows/{workflowId}/metadata", async httpContext =>
                {
                    var workflowName = httpContext.GetRouteValue("workflowId") as string;

                    // Security
                    var authorize = httpContext.RequestServices.GetRequiredService<IAuthorizationService>();
                    var auth = await authorize.AuthorizeAsync(httpContext.User,
                        new EavWorkflowResource { WorkflowName = workflowName },
                        new RunWorkflowRequirement(workflowName));

                    if (!auth.Succeeded)
                    {
                        await new AuthorizationEndpointResult(new
                        {
                            errors = auth.Failure.FailedRequirements.OfType<IAuthorizationRequirementError>()
                                    .Select(c => c.ToError())
                        })
                            .ExecuteAsync(httpContext);
                        return;
                    }

                    // Workflow
                    var workflows = httpContext.RequestServices.GetRequiredService<IEnumerable<IWorkflow>>();
                    var workflow = workflows.FirstOrDefault(n => string.Equals(n.Id.ToString(), workflowName, StringComparison.OrdinalIgnoreCase) || string.Equals(n.GetType().Name, workflowName, StringComparison.OrdinalIgnoreCase));

                    var isIInitializable =
                            workflow.GetType()
                                   .GetInterfaces()
                                   .Any(i => i.IsGenericType &&
                                             i.GetGenericTypeDefinition() == typeof(IWorkflowInputs<>));

                    if (isIInitializable)
                    {
                        var t = workflow.GetType()
                               .GetInterfaces()
                               .First(i => i.IsGenericType &&
                                           i.GetGenericTypeDefinition() == typeof(IWorkflowInputs<>))
                               .GetGenericArguments()
                               .First();

                        var form = JsonSchemaForm.FromType(t).Build();

                        await httpContext.Response.WriteJsonAsync(form);
                    }
                    else
                    {

                    }
                });
            }

            if (options.Value.IncludeStartWorkflow)
            {
                endpoints.MapPost("/api/workflows/{workflowId}/runs", async httpContext =>
                {
                    var workflowName = httpContext.GetRouteValue("workflowId") as string;
                    
                    // Security
                    var authorize = httpContext.RequestServices.GetRequiredService<IAuthorizationService>();
                    var auth = await authorize.AuthorizeAsync(httpContext.User,
                        new EavWorkflowResource { WorkflowName = workflowName },
                        new RunWorkflowRequirement(workflowName));

                    if (!auth.Succeeded)
                    {
                        await new AuthorizationEndpointResult(new
                            {
                                errors = auth.Failure.FailedRequirements.OfType<IAuthorizationRequirementError>()
                                    .Select(c => c.ToError())
                            })
                            .ExecuteAsync(httpContext);
                        return;
                    }
                    
                    // Workflow
                    var workflows = httpContext.RequestServices.GetRequiredService<IEnumerable<IWorkflow>>();
                    
                    var record =
                        await JToken.ReadFromAsync(
                            new JsonTextReader(new StreamReader(httpContext.Request.BodyReader.AsStream())));
                    
                    var inputs = new Dictionary<string, object> { ["data"] = record, ["payload"] = record };

                    var trigger = await BuildTrigger(workflows, workflowName, httpContext.User?.FindFirstValue("sub"), inputs);
                    
                    if (trigger == null)
                    {
                        httpContext.Response.StatusCode = 404;
                        return;
                    }
                    var context = httpContext.RequestServices.GetRequiredService<EAVDBContext<TContext>>();
                    context.Add(new TWorkflowRun() { Id = trigger.RunId });

                    await context.SaveChangesAsync(httpContext.User);

                    var backgroundJobClient = httpContext.RequestServices.GetRequiredService<IBackgroundJobClient>();
                    var job = backgroundJobClient.Enqueue<IHangfireWorkflowExecutor>(
                        executor => executor.TriggerAsync(trigger,null));


                    await httpContext.Response.WriteJsonAsync(new { id= trigger.RunId, job = job });
                });
                
                endpoints.MapPost("/api/entities/{entityName}/records/{recordId}/workflows/{workflowId}/runs", async (httpcontext) =>
                {
                    var context = httpcontext.RequestServices.GetRequiredService<EAVDBContext<TContext>>();
                    var entityName = httpcontext.GetRouteValue("entityName") as string;
                    var recordId = httpcontext.GetRouteValue("recordId") as string;
                    var workflowname = httpcontext.GetRouteValue("workflowId") as string;

                    var type = context.Context.GetEntityType(entityName);
                    var resource = new EAVResource
                    {
                        EntityType = type,
                        EntityCollectionSchemaName = type.GetCustomAttribute<EntityAttribute>().CollectionSchemaName
                    };

                    var authorize = httpcontext.RequestServices.GetRequiredService<IAuthorizationService>();
                    var auth = await authorize.AuthorizeAsync(httpcontext.User, resource,
                        new RunRecordWorkflowRequirement(entityName, recordId, workflowname));

                    if (!auth.Succeeded)
                    {
                        await new AuthorizationEndpointResult(new
                            {
                                errors = auth.Failure.FailedRequirements.OfType<IAuthorizationRequirementError>()
                                    .Select(c => c.ToError())
                            })
                            .ExecuteAsync(httpcontext);

                        return;
                    }

                    //Run custom workflow
                    var backgroundJobClient = httpcontext.RequestServices.GetRequiredService<IBackgroundJobClient>();
                    var record =
                        await JToken.ReadFromAsync(
                            new JsonTextReader(new StreamReader(httpcontext.Request.BodyReader.AsStream())));

                    var inputs = new Dictionary<string, object>
                    {
                        ["entityName"] = entityName,
                        ["recordId"] = recordId,
                        ["data"] = record,
                        ["payload"] = record
                    };

                    var workflows = httpcontext.RequestServices.GetRequiredService<IEnumerable<IWorkflow>>();

                    var trigger = await BuildTrigger(workflows, workflowname, httpcontext.User?.FindFirstValue("sub"), inputs);

                    context.Add(new TWorkflowRun() { Id=trigger.RunId });

                    await context.SaveChangesAsync(httpcontext.User);

                    var job = backgroundJobClient.Enqueue<IHangfireWorkflowExecutor>(
                        (executor) => executor.TriggerAsync(trigger,null));

                    await httpcontext.Response.WriteJsonAsync(new { id = trigger.RunId, job = job });
                }).WithMetadata(new AuthorizeAttribute("EAVAuthorizationPolicy"));
            }
            
            if (options.Value.IncludeWorkflowState)
            {
                endpoints.MapGet("/api/workflowruns/{workflowRunId}/status", async context =>
                    await ApiWorkflowsEndpoint<TContext, TWorkflowRun>(context, true));

                endpoints.MapGet("/api/workflowruns/{workflowRunId}",
                    async context => await ApiWorkflowsEndpoint<TContext, TWorkflowRun>(context));

                endpoints.MapGet("/api/workflows/{workflowId}/runs/{workflowRunId}",
                    async context => await ApiWorkflowsEndpoint<TContext, TWorkflowRun>(context));

                endpoints.MapGet("/api/workflows/{workflowId}/runs/{workflowRunId}/status",
                   async context => await ApiWorkflowsEndpoint<TContext, TWorkflowRun>(context,true));


            }
            
            return endpoints;
        }

        private static async Task ApiWorkflowsEndpoint<TContext, TWorkflowRun>(HttpContext context, bool statusOnly = false) where TContext : DynamicContext
            where TWorkflowRun : DynamicEntity, IWorkflowRun
        {
            var routeJobId = context.GetRouteValue("workflowRunId") as string;
            if (!Guid.TryParse(routeJobId, out var jobId))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync($"'{routeJobId}' is not a valid guid");
                return;
            }

            var db = context.RequestServices.GetRequiredService<EAVDBContext<TContext>>();

            var workflowRun = await db.Set<TWorkflowRun>().FindAsync(jobId);

            if (workflowRun == null)
            {
                await new NotFoundResult().ExecuteAsync(context);
                return;
            }

            if(workflowRun.State == null)
            {
                await new DataEndpointResult(new { completed = false }).ExecuteAsync(context);
                return;
            }

            var workflowRunState = await GetState(workflowRun);
            
            if (statusOnly)
            {
                var completed = workflowRunState.Events.Any(x => x.EventType == EventType.WorkflowFinished);
                 
                await new DataEndpointResult(new { completed, body=workflowRunState.Body, failedreason = workflowRunState.FailedReason }).ExecuteAsync(context);
            }
            else
            {

                await new DataEndpointResult(workflowRunState).ExecuteAsync(context);
            }
        }

        private static Task<TriggerContext> BuildTrigger(
            IEnumerable<IWorkflow> workflows,
            string workflowName,
            string principalId,
            Dictionary<string, object> inputs
            )
        {
            var workflow = workflows.FirstOrDefault(n => string.Equals(n.Id.ToString(), workflowName, StringComparison.OrdinalIgnoreCase) || string.Equals(n.GetType().Name, workflowName, StringComparison.OrdinalIgnoreCase));

            if (workflow == null)
                return null;
            
            
            var trigger = new TriggerContext
            {
                RunId = Guid.NewGuid(),
                Workflow = workflow,
                PrincipalId = principalId,
                Trigger = new Trigger
                {
                    Inputs = inputs,
                    ScheduledTime = DateTimeOffset.UtcNow,
                    Type = workflow.Manifest.Triggers.FirstOrDefault().Value.Type,
                    Key = workflow.Manifest.Triggers.FirstOrDefault().Key
                },
            };
            

            return Task.FromResult( trigger);
        }

        private static Task<WorkflowState> GetState(IWorkflowRun run)
        {
            using var tinyStream = new JsonTextReader(
            new StreamReader(new GZipStream(new MemoryStream(run.State), CompressionMode.Decompress)));

            var serializer = JsonSerializer.CreateDefault(new JsonSerializerSettings { 
                ContractResolver = new CamelCasePropertyNamesContractResolver(), 
                NullValueHandling= NullValueHandling.Ignore });
            return Task.FromResult(serializer.Deserialize<WorkflowState>(tinyStream));
        }
        
        


        public static IEAVFrameworkBuilder AddWorkFlowEngine<TContext, TWorkflowRun>(
            this IEAVFrameworkBuilder builder,
            string workflowContextPrincipalId, 
            Func<IServiceProvider,IGlobalConfiguration, IGlobalConfiguration> configureHangfire = null, bool withJobServer=true)
          where TContext : DynamicContext
          where TWorkflowRun : DynamicEntity, IWorkflowRun, new()
        {
            var services = builder.Services;

            services.AddExpressionEngine();
            services.AddWorkflowEngine<EAVFWOutputsRepository<TContext,TWorkflowRun>>();
            services.AddOptions<WorkflowEndpointOptions>().BindConfiguration("EAVFramework:WorkflowEngine");
            
            builder.Services.AddOptions<EAVFWOutputsRepositoryOptions>()
              .Configure(c =>
              {
                  c.IdenttyId = workflowContextPrincipalId;
              });

            services.AddFunctions();

            services.AddTransient(typeof(IHangFirePluginJobRunner<>), typeof(HangFirePluginJobRunner<>));
            services.AddScoped(typeof(IPluginScheduler<>), typeof(HangFirePluginScheduler<>));
            
            
            services.AddTransient<IHangFirePluginSchedulerAsyncContextFactory, DefaultHangFirePluginSchedulerAsyncContextFactory>();


            configureHangfire = configureHangfire ?? NullOp;
            services.AddHangfire((sp, configuration) =>
            {

                SetupConnection(configureHangfire(sp, configuration
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()), sp.GetRequiredService<IConfiguration>());
               
             
                configuration.UseFilter(new HangfireWorkflowManifestJobFilter(sp.GetService<IWorkflowAccessor>()));
            });

            if(withJobServer)
                services.AddHangfireServer();

            return builder;
        }
        static IGlobalConfiguration NullOp(IServiceProvider sp, IGlobalConfiguration config) => config;
        private static void SetupConnection(IGlobalConfiguration globalConfiguration, IConfiguration configuration)
        {
            var options = new SqlServerStorageOptions
            {
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.Zero,
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true,
                SchemaName = configuration.GetValue<string>("Hangfire:DBSchema") ?? "hangfire",

            };
              
            var connectionstring = configuration.GetConnectionString("ApplicationDB");
            if (connectionstring.Contains("authentication", StringComparison.OrdinalIgnoreCase))
            {
                globalConfiguration.UseSqlServerStorage(() => new Microsoft.Data.SqlClient.SqlConnection(
        connectionstring), options);
            }
            else
            { 
                globalConfiguration.UseSqlServerStorage(connectionstring, options);

            }

            // JobStorage.Current = a.Entry;
        }
    }
}
