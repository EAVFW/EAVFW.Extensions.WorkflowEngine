using EAVFramework.Endpoints;
using EAVFramework.Validation;
using Microsoft.AspNetCore.Authorization;

namespace EAVFW.Extensions.WorkflowEngine.Endpoints
{
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



        public AuthorizationError ToError()
        {
            return new AuthorizationError
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
}
