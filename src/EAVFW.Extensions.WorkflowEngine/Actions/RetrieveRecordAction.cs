using EAVFramework;
using EAVFramework.Endpoints;
using ExpressionEngine;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorkflowEngine.Core;

namespace EAVFW.Extensions.WorkflowEngine.Actions
{
    public class RetrieveRecordAction<TContext> : IActionImplementation
        where TContext: DynamicContext
    {
        private readonly EAVDBContext<TContext> _database;
        private readonly ILogger<RetrieveRecordAction<TContext>> _logger;
        private readonly IExpressionEngine _expressionEngine;

        public RetrieveRecordAction(EAVDBContext<TContext> database, ILogger<RetrieveRecordAction<TContext>> logger, IExpressionEngine expressionEngine)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _logger = logger;
            _expressionEngine = expressionEngine;
        }

        public async ValueTask<object> ExecuteAsync(IRunContext context, IWorkflow workflow, IAction action)
        {

            var inputs = action.Inputs;

            var data = await _database.FindAsync(inputs["entityName"]?.ToString(), Guid.Parse(inputs["recordId"]?.ToString()));


            return data.Entity;



        }
    }
}
