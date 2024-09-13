using EAVFramework.Extensions;
using ExpressionEngine;
using ExpressionEngine.Functions.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EAVFW.Extensions.WorkflowEngine.Expresions
{
    public class UrlSafeBase64EncodeFunction : IFunction
    {
        public  ValueTask<ValueContainer> ExecuteFunction(params ValueContainer[] parameters)
        {
            var input = parameters[0].GetValue<string>();
            var bytes = Encoding.UTF8.GetBytes(input);
            return ValueTask.FromResult(new ValueContainer(Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').Replace("=", "")));
        }
    }
    public class UrlSafeHashFunction : IFunction
    {
        public   ValueTask<ValueContainer> ExecuteFunction(params ValueContainer[] parameters)
        {
          
            var input = parameters[0].GetValue<string>();
            return ValueTask.FromResult(new ValueContainer(input.URLSafeHash()));
          
        }
    }
}
