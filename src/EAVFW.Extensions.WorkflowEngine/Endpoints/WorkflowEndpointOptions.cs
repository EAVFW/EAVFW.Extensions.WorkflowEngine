namespace EAVFW.Extensions.WorkflowEngine.Endpoints
{
    public class WorkflowEndpointOptions
    {
        public bool IncludeListWorkflows { get; set; } = false;
        public bool IncludeStartWorkflow { get; set; } = true;
        public bool IncludeWorkflowState { get; set; } = true;
        public bool IncludeWorkflowMetadata { get; set; } = true;
    }
}
