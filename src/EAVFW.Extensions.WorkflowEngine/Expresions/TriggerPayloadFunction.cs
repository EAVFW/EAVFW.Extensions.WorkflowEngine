using ExpressionEngine;
using ExpressionEngine.Functions.Base;
using System.Threading.Tasks;

namespace WorkflowEngine.Core.Expressions
{
    public class TriggerPayloadFunction : IFunction
    {
        private readonly TriggerBodyFunction _triggerBodyFunction;

        public TriggerPayloadFunction(TriggerBodyFunction triggerBodyFunction)
        {
     
            _triggerBodyFunction = triggerBodyFunction;
        }
        public async ValueTask<ValueContainer> ExecuteFunction(params ValueContainer[] parameters)
        {
            var trigger = await _triggerBodyFunction.ExecuteFunction();

            return trigger["payload"];


        }
    }
}
