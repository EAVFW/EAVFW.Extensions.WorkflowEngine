using EAVFramework;
using EAVFramework.Endpoints;
using EAVFramework.Plugins;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;

namespace EAVFW.Extensions.WorkflowEngine
{
    public class HangFirePluginJobRunnerContext : IFormattable
    {
        static HangFirePluginJobRunnerContext()
        {
            ExecuteAsyncMethod = typeof(HangFirePluginJobRunnerContext).GetMethods().Single(m => m.IsGenericMethod && m.GetGenericArguments().Length == 2 && m.Name == nameof(HangFirePluginJobRunnerContext.ExecuteAsync));
        }

        public static MethodInfo ExecuteAsyncMethod { get; }

        //public Guid EntityId { get; set; }
        //public string EntityType { get; set; }
        public string IdentityId { get; set; }
        public Type Handler { get; set; }
        public string SchemaName { get; set; }


        public async Task<PluginContext> ExecuteAsync<TContext>(IServiceProvider serviceProvider, EAVDBContext<TContext> context, EntityEntry entry)
         where TContext : DynamicContext
        {
            var executeTask = (Task<PluginContext>)ExecuteAsyncMethod
                .MakeGenericMethod(typeof(TContext), entry.Entity.GetType())
                .Invoke(this, new object[] { serviceProvider, context, entry });

            return await executeTask;

        }
        public async Task<PluginContext> ExecuteAsync<TContext, T>(IServiceProvider serviceProvider, EAVDBContext<TContext> context, EntityEntry entry)
              where TContext : DynamicContext
        where T : DynamicEntity

        {
            var pluginContext = PluginContextFactory.CreateContext<TContext, T>(context, entry, new System.Security.Claims.ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", IdentityId) }, "eavfw")));

            var handler = serviceProvider.GetService(Handler) as IPlugin<TContext, T>;
            //TODO mix of context types;
            if (handler != null)
                await handler.Execute(pluginContext);


            await pluginContext.DB.SaveChangesAsync(pluginContext.User);

            return pluginContext;



        }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == "Handler")
            {
                return Handler.Name;
            }

            return "Unknown format string";
        }
    }
}