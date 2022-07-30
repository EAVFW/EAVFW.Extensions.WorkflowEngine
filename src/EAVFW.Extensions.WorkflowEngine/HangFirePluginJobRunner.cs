using DotNetDevOps.Extensions.EAVFramework;
using DotNetDevOps.Extensions.EAVFramework.Endpoints;
using DotNetDevOps.Extensions.EAVFramework.Plugins;
using DotNetDevOps.Extensions.EAVFramework.Shared;
using Hangfire;
using Hangfire.Server;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace EAVFW.Extensions.WorkflowEngine
{
    public interface IHangFirePluginJobRunner<TContext> where TContext : DynamicContext
    {
        Task ExecuteAsync(string entityType, object[] keys, HangFirePluginJobRunnerContext data, PerformContext jobcontext);
    }

    public class HangFirePluginJobRunner<TContext> : IHangFirePluginJobRunner<TContext>
        where TContext : DynamicContext
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<HangFirePluginJobRunner<TContext>> _logger;

        public HangFirePluginJobRunner(IServiceProvider serviceProvider, ILogger<HangFirePluginJobRunner<TContext>> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [JobDisplayName("{2:Handler}: {0}({1})")]
        public async Task ExecuteAsync(string entityType, object[] keys, HangFirePluginJobRunnerContext data, PerformContext jobcontext)
        {  
            using (_logger.BeginScope(new Dictionary<string, string>()
            {
                ["JobId"] = jobcontext.BackgroundJob.Id,
                ["EntityType"] = entityType,
                ["EntityId"] = String.Join(",",keys),
                ["CreatedAt"] = jobcontext.BackgroundJob.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["ContextType"] = typeof(TContext).Name
            }))
            {
                 
                try
                {
                    _logger.LogInformation("Starting execution of async plugin");

                    var db = _serviceProvider.GetRequiredService<EAVDBContext<TContext>>();
                    
                    var entryTypeMetadata = db.Context.Model.FindEntityType(db.Context.GetEntityType(entityType).FullName);
                    var typedKeys= entryTypeMetadata.FindPrimaryKey().Properties.Select((p,i)=> ConvertType(keys[i],p.ClrType)).ToArray();
                     
                    var record = await db.FindAsync(entityType, typedKeys);
                    var entry = db.Context.Entry(record);

                    var ctx = await data.ExecuteAsync<TContext>(_serviceProvider, db, entry);

                    if (ctx.Errors.Any())
                    {
                        _logger.LogWarning("Plugin ran with errors: {errors}", string.Join(",", ctx.Errors.Select(err => err.Code)));
                    } 

                }
                catch (Exception ex)
                {

                    throw;
                }
                finally
                {
                    _logger.LogDebug("Finished execution of async plugin");

                }
            }
        }

        private object ConvertType(object v, Type clrType)
        {
            switch (v)
            {
                case string str when clrType == typeof(Guid):
                    return Guid.Parse(str);

            }

            return v;
        }
    }
}