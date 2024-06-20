using EAVFW.Extensions.WorkflowEngine.Abstractions;
using WorkflowEngine.Core;

namespace EAVFW.Extensions.WorkflowEngine.Models
{
    public class Workflow<TInput> : Workflow, IWorkflowInputs<TInput>
       where TInput : class
    {

    }
}
