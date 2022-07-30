using DotNetDevOps.Extensions.EAVFramework.Shared;
using System;

namespace EAVFW.Extensions.WorkflowEngine
{
    [EntityInterface(EntityKey = "Workflow Run")]
    public interface IWorkflowRun
    {
        Byte[] State { get; set; }
        Guid Id { get; set; }
    }
}
