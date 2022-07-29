using System.Threading.Tasks;

namespace EAVFW.Extensions.WorkflowEngine
{
    public class DefaultHangFirePluginSchedulerAsyncContextFactory : IHangFirePluginSchedulerAsyncContextFactory
    {
        public ValueTask<HangFirePluginJobRunnerContext> CreateContextAsync()=> new ValueTask<HangFirePluginJobRunnerContext>(new HangFirePluginJobRunnerContext());
    }
}