
using EAVFramework;
using EAVFramework.Configuration;
using EAVFramework.Endpoints;
using EAVFramework.Endpoints.Results;
using EAVFramework.Extensions;
using EAVFramework.Plugins;
using EAVFramework.Shared;
using EAVFramework.Validation;
using EAVFW.Extensions.WorkflowEngine;
using ExpressionEngine;
using Hangfire;
using Hangfire.Dashboard;
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

namespace Microsoft.Extensions.DependencyInjection
{
    public static class HangfireExtensions
    {
        public static IEndpointRouteBuilder MapAuthorizedHangfireDashboard(
            this IEndpointRouteBuilder endpoints, 
            string url = "/.well-known/jobs",
            string policyName = "HangfirePolicyName")
        {

            endpoints.MapHangfireDashboard(url, new DashboardOptions()
            {
                Authorization = new List<IDashboardAuthorizationFilter> { }
            })
           .RequireAuthorization(policyName);


            return endpoints;
        }
        public static AuthorizationOptions AddHangfirePolicy(this AuthorizationOptions options,
            string policyName = "HangfirePolicyName",
            string schemaName = "eavfw",
            string role = "System Administrator")
        {
            options.AddPolicy(policyName, pb =>
            {
               
                    pb.AddAuthenticationSchemes(schemaName);
                    pb.RequireAuthenticatedUser();
                    pb.RequireClaim("role", role);
                
            });

            return options;
        }

        public static AuthorizationOptions AddHangfireAnonymousPolicy(this AuthorizationOptions options,
              string policyName = "HangfirePolicyName")
        {
            options.AddPolicy(policyName, pb =>
            {
                pb.RequireAssertion(c => true);
            });

            return options;
        }

    }

    public class RunRecordWorkflowRequirement : IAuthorizationRequirement, IAuthorizationRequirementError
    {
        public string RecordId { get; }
        public string WorkflowName { get; }

        public string EntityCollectionSchemaName { get; }
        public RunRecordWorkflowRequirement(string entityName, string recordId, string workflowname) 
        {
            EntityCollectionSchemaName = entityName;
            RecordId = recordId;
            WorkflowName = workflowname;
        }

       

        public ValidationError ToError()
        {
            return new ValidationError
            {
                Error = "No permission to run workflow",
                Code = "NO_RUN_WORKFLOW_PERMISSION",
                ErrorArgs = new[]
                {
                    EntityCollectionSchemaName
                },
                EntityCollectionSchemaName = EntityCollectionSchemaName

            };
        }
    }
    
    public class RunWorkflowRequirement : IAuthorizationRequirement, IAuthorizationRequirementError
    {
        public string WorkflowName { get; }

        public RunWorkflowRequirement(string workflowName) 
        {
            WorkflowName = workflowName;
        }

        public ValidationError ToError()
        {
            return new ValidationError
            {
                Error = "No permission to run workflow",
                Code = "NO_RUN_WORKFLOW_PERMISSION"
            };
        }
    }

    public static class DependencyInjectionExtensions
    {
        public static IEndpointRouteBuilder MapWorkFlowEndpoints<TContext, TWorkflowRun>(
            this IEndpointRouteBuilder endpoints)
            where TContext : DynamicContext
            where TWorkflowRun : DynamicEntity, IWorkflowRun
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
                    
                    var inputs = new Dictionary<string, object> { ["data"] = record };

                    var trigger = await BuildTrigger(workflows, workflowName, httpContext.User?.FindFirstValue("sub"), inputs);
                    
                    if (trigger == null)
                    {
                        httpContext.Response.StatusCode = 404;
                        return;
                    }
                    
                    var backgroundJobClient = httpContext.RequestServices.GetRequiredService<IBackgroundJobClient>();
                    var job = backgroundJobClient.Enqueue<IHangfireWorkflowExecutor>(
                        executor => executor.TriggerAsync(trigger));

                    await httpContext.Response.WriteJsonAsync(new { id = job });
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
                        ["data"] = record
                    };

                    var workflows = httpcontext.RequestServices.GetRequiredService<IEnumerable<IWorkflow>>();

                    var trigger = await BuildTrigger(workflows, workflowname, httpcontext.User?.FindFirstValue("sub"), inputs);
                    
                    var job = backgroundJobClient.Enqueue<IHangfireWorkflowExecutor>(
                        (executor) => executor.TriggerAsync(trigger));

                    await httpcontext.Response.WriteJsonAsync(new { id = job });
                }).WithMetadata(new AuthorizeAttribute("EAVAuthorizationPolicy"));
            }
            
            if (options.Value.IncludeWorkflowState)
            {
                endpoints.MapGet("/api/workflowruns/{workflowRunId}/status", async context =>
                    await ApiWorkflowsEndpoint<TContext, TWorkflowRun>(context, true));

                endpoints.MapGet("/api/workflowruns/{workflowRunId}",
                    async context => await ApiWorkflowsEndpoint<TContext, TWorkflowRun>(context));
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

            var workflowRunState = await GetState(workflowRun);
            
            if(statusOnly)
            {
                var done = workflowRunState.SelectToken(
                    $"$.events[?(@.EventType == {(int) EventType.WorkflowFinished})]");

                var status = done != null;

                await new DataEndpointResult(new { status }).ExecuteAsync(context);
            }
            else
            {
                await new DataEndpointResult(workflowRunState).ExecuteAsync(context);
            }
        }

        private static async Task<TriggerContext> BuildTrigger(
            IEnumerable<IWorkflow> workflows,
            string workflowName,
            string principalId,
            Dictionary<string, object> inputs
            )
        {
            var workflow = workflows.FirstOrDefault(n =>
                n.Id.ToString() == workflowName ||
                string.Equals(n.GetType().Name, workflowName, StringComparison.OrdinalIgnoreCase));

            if (workflow == null)
                return null;
            
            workflow.Manifest = null;
            var trigger = new TriggerContext
            {
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
            
            return trigger;
        }

        private static async Task<JObject> GetState(IWorkflowRun run)
        {
            using var tinyStream = new JsonTextReader(
            new StreamReader(new GZipStream(new MemoryStream(run.State), CompressionMode.Decompress)));

            var serializer = JsonSerializer.CreateDefault();
            return serializer.Deserialize<JObject>(tinyStream);
        }
        
        


        public static IEAVFrameworkBuilder AddWorkFlowEngine<TContext, TWorkflowRun>(
            this IEAVFrameworkBuilder builder,
            string workflowContextPrincipalId, 
            Func<IServiceProvider,IGlobalConfiguration, IGlobalConfiguration> configureHangfire = null)
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
            services.AddHangfire((sp, configuration) => SetupConnection(configureHangfire(sp,configuration
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()), sp.GetRequiredService<IConfiguration>())
                );


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
