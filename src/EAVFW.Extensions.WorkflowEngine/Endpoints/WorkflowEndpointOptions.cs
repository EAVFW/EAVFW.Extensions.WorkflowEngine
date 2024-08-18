using EAVFramework.Endpoints;
using System;
using System.Threading.Tasks;

namespace EAVFW.Extensions.WorkflowEngine.Endpoints
{
    public class WorkflowEndpointOptions
    {
        public bool IncludeListWorkflows { get; set; } = false;
        public bool IncludeStartWorkflow { get; set; } = true;
        public bool IncludeWorkflowState { get; set; } = true;
        public bool IncludeWorkflowMetadata { get; set; } = true;

        public string QueueName { get; set; } = "default";

        public Func<EAVDBContext,Guid, IWorkflowRun> RunFactory { get; set; }
        public string WorkflowEntityName { get;  set; }
    }
}
