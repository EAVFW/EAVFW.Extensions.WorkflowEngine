using DotNetDevOps.Extensions.EAVFramework.Shared;
using EAVFW.Extensions.Documents;
using System;

namespace EAVFW.Extensions.WorkflowEngine
{
    [EntityInterface(EntityKey = "Workflow Run")]
    public interface IWorkflowRun
    {
        Byte[] State { get; set; }
        Guid Id { get; set; }
    }

    [EntityInterface(EntityKey = "Workflow")]
    public interface IWorkflow<T> where T : IDocumentEntity
    {
        public Guid Id { get; set; }        
        public string Version { get; set; }
        public Guid? ManifestId { get; set; }
        public T Manifest { get; set; }
    }
}
