namespace EAVFW.Extensions.WorkflowEngine
{
    public class WorkflowEndpointOptions
    {
        public bool IncludeListWorkflows { get; set; } = false;
        public bool IncludeStartWorkflow { get; set; } = false;
        public bool IncludeWorkflowState { get; set; } = false;
    }
}
