using EAVFramework;
using EAVFramework.Plugins;
using EAVFramework.Shared;
using Hangfire;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace EAVFW.Extensions.WorkflowEngine
{
    public class HangFirePluginScheduler<TContext> : IPluginScheduler<TContext>
        where TContext : DynamicContext
       
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IHangFirePluginSchedulerAsyncContextFactory _contextFactory;
        private readonly TContext _context;
        private readonly ILogger<HangFirePluginScheduler<TContext>> _logger;
        private readonly IBackgroundJobClient _backgroundJobClient;

        public HangFirePluginScheduler(
            IServiceProvider serviceProvider,
            IHangFirePluginSchedulerAsyncContextFactory contextFactory,
            TContext context,
            ILogger<HangFirePluginScheduler<TContext>> logger,
            IBackgroundJobClient backgroundJobClient)
        {
            this._serviceProvider = serviceProvider;
            this._contextFactory = contextFactory;
            
            this._context = context;
            this._logger = logger;
            _backgroundJobClient = backgroundJobClient ?? throw new ArgumentNullException(nameof(backgroundJobClient));
        }
        public async Task ScheduleAsync(EntityPlugin plugin, string identityid, object entity)
        {
            var entry = _context.Entry(entity);

            /**
             * The Async context is the serializable context that contains state passed to the async running context
             */

            var asyncContext = await _contextFactory.CreateContextAsync();

            asyncContext.IdentityId = identityid;
            asyncContext.Plugin = plugin;
            asyncContext.Handler = plugin.Handler;

            var collectionSchemaName = entry.Entity.GetType().GetCustomAttribute<EntityAttribute>().CollectionSchemaName;
            var keys = entry.Metadata.FindPrimaryKey().Properties.Select(p => p.PropertyInfo.GetValue(entity)).ToArray();
             
            _logger.LogDebug("Scheduling async plugin for {@asyncContext}", asyncContext);
           
            var id = _backgroundJobClient.Enqueue<IHangFirePluginJobRunner<TContext>>(k =>
                    k.ExecuteAsync(collectionSchemaName, new RecordKeys { Values = keys }, asyncContext, null));

            _logger.LogInformation("Async plugin scheduled for {@asyncContext} with {id}", asyncContext, id);
             
        }

    }
}