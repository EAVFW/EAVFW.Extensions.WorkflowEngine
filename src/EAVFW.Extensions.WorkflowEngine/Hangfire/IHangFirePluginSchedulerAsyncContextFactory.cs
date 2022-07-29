using System.Threading.Tasks;

namespace EAVFW.Extensions.WorkflowEngine
{
    public interface IHangFirePluginSchedulerAsyncContextFactory
          
    {
        public ValueTask<HangFirePluginJobRunnerContext> CreateContextAsync();
    }
}