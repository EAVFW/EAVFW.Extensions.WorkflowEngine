using EAVFramework.Endpoints;
using EAVFramework.Validation;
using Microsoft.AspNetCore.Authorization;

namespace EAVFW.Extensions.WorkflowEngine.Endpoints
{
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
}
