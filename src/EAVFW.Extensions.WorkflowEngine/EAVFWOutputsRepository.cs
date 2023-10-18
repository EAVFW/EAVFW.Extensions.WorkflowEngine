
using EAVFramework;
using EAVFramework.Endpoints;
using EAVFW.Extensions.WorkflowEngine.Models;
using ExpressionEngine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using WorkflowEngine.Core;

namespace EAVFW.Extensions.WorkflowEngine
{

    public class DictionaryIgnoreNullValueConverter : JsonConverter
    {
        public override bool CanRead => false;

        public override bool CanConvert(Type objectType)
        {
            return typeof(IDictionary<string, object>).IsAssignableFrom(objectType);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var dictionary = (IDictionary<string, object>) value;

            writer.WriteStartObject();

            foreach (var pair in dictionary)
            {
                if (pair.Value == null)
                    continue;
                if (pair.Value is ValueContainer c && c.Type() == ExpressionEngine.ValueType.Null)
                    continue;


                writer.WritePropertyName(pair.Key);

                serializer.Serialize(writer, pair.Value);

            }

            writer.WriteEndObject();
        }
    }

    public class EAVFWOutputsRepository<TContext, TWorkflowRun> : IOutputsRepository, IDisposable
        where TContext : DynamicContext
        where TWorkflowRun : DynamicEntity, IWorkflowRun, new()
    {
        private readonly IServiceScope _scope;
        private readonly EAVDBContext<TContext> _eAVDBContext;
        private JsonSerializerSettings CreateSerializerSettings()
        {
            var settings = JsonConvert.DefaultSettings();
            settings.NullValueHandling = NullValueHandling.Ignore;
            settings.Converters.Add(new DictionaryIgnoreNullValueConverter());
            return settings;
        }

      
        private JsonSerializerSettings _serializerSettings;
        private JsonSerializer _serializer;

        protected ClaimsPrincipal Principal { get; }

        public EAVFWOutputsRepository(IServiceScopeFactory scopeFactory, IOptions<EAVFWOutputsRepositoryOptions> options)
        {
            _scope = scopeFactory.CreateScope(); 
            _serializerSettings = CreateSerializerSettings();
            _serializer = JsonSerializer.Create(_serializerSettings);
            _eAVDBContext = _scope.ServiceProvider.GetRequiredService<EAVDBContext<TContext>>();
            Principal = new ClaimsPrincipal(new ClaimsIdentity(new Claim[] {
                                   new Claim("sub",options.Value.IdenttyId)
                                }, "eavfw"));
        }
        public async ValueTask AddScopeItem(IRunContext context, IWorkflow workflow, IAction action, IActionResult result)
        {
            await AddAsync(context, workflow, action, result);

        }
        public async ValueTask AddAsync(IRunContext context, IWorkflow workflow, IAction action, IActionResult result)
        {
            var run = await GetOrCreateRun(context);

            /*
             * The run object is the full state object, where the obj is the specific action.
             * The action is added to the run and then its the saved after.
             */
            var obj = GetOrCreateStateObject(action.Key, run);
            obj.UpdateFrom(result);
            

            /*
             * 
             * Saving the updated run payload with action added to obj.
             */
            await SaveState(context.RunId, run);

        }

        private static ActionState GetOrCreateStateObject(string key, WorkflowState run, bool remove=false)
        {
            var actions = run.Actions;// run["actions"];

            var idx = key.IndexOf('.');
           
            while (idx != -1)
            {
                var subactions = actions[key.Substring(0, idx)];
                key = key.Substring(idx + 1);
                idx = key.IndexOf('.');


                actions = subactions.Actions;
            }

           
            if (!actions.ContainsKey(key))
            {
                actions[key] = new ActionState();
            }
            var obj = actions[key]; 

            if (remove)
            {
                actions.Remove(key);
            }
            return obj;
        }
        private async Task SaveState(Guid runId, WorkflowState state)
        {
            using var ms = new MemoryStream();
            using var tinyStream = new GZipStream(ms, CompressionMode.Compress);
            using var writer = new JsonTextWriter(new StreamWriter(tinyStream));

            _serializer.Serialize(writer, state);


            writer.Flush();
            tinyStream.Flush();

            var run = await _eAVDBContext.Set<TWorkflowRun>().FindAsync(runId);

            run.State = ms.ToArray();

            _eAVDBContext.Set<TWorkflowRun>().Update(run);
            await _eAVDBContext.SaveChangesAsync(Principal);
        }

        public async Task<WorkflowState> GetState(Guid runId)
        {
            var run = await _eAVDBContext.Set<TWorkflowRun>().FindAsync(runId);
            using var tinyStream = new JsonTextReader(
                new StreamReader(new GZipStream(new MemoryStream(run.State), CompressionMode.Decompress)));


            return _serializer.Deserialize<WorkflowState>(tinyStream);
        }

        private async Task<WorkflowState> GetOrCreateRun(IRunContext context)
        {
            var run = await _eAVDBContext.Set<TWorkflowRun>().FindAsync(context.RunId);
            if (run == null)
            {
                var (data, dataarr) = CreateState();

                run = new TWorkflowRun() { Id = context.RunId, State = dataarr };
                _eAVDBContext.Set<TWorkflowRun>().Add(run);
                await _eAVDBContext.SaveChangesAsync(Principal);

                return data;
            }

            if (run.State == null)
            {
                var (data, dataarr) = CreateState();
                run.State = dataarr;
                await _eAVDBContext.SaveChangesAsync(Principal);
                return data;
            }

            {

                using var tinyStream = new JsonTextReader(new StreamReader(new GZipStream(new MemoryStream(run.State), CompressionMode.Decompress)));


                return _serializer.Deserialize<WorkflowState>(tinyStream);


            }

            (WorkflowState, byte[]) CreateState()
            {
                //var data = new JObject(new JProperty("status", "Running"), new JProperty("principal", context.PrincipalId), new JProperty("actions", new JObject()), new JProperty("triggers", new JObject()));
                var data = new WorkflowState() { Status = WorkflowStateStatus.Running, Principal = context.PrincipalId };
                using var ms = new MemoryStream();
                using var tinyStream = new GZipStream(ms, CompressionMode.Compress);
                using var writer = new JsonTextWriter(new StreamWriter(tinyStream));
                _serializer.Serialize(writer, data);
                writer.Flush();
                tinyStream.Flush();


                return (data, ms.ToArray());
            }

            // return Runs.GetOrAdd(context.RunId, (id) => new JObject(new JProperty("actions", new JObject()), new JProperty("triggers", new JObject())));
        }

        public async ValueTask AddTrigger(ITriggerContext context, IWorkflow workflow, ITrigger trigger)
        {
            var run = await GetOrCreateRun(context);
           
            run.Triggers[trigger.Key] = JToken.FromObject(new { time = trigger.ScheduledTime, body = trigger.Inputs, jobId = context.JobId, type = trigger.Type });

            await SaveState(context.RunId, run);
        }


        public async ValueTask<object> GetTriggerData(Guid id)
        {
            var run = await GetState(id);
            return run.Triggers.FirstOrDefault().Value;
            //return run["triggers"].OfType<JProperty>().FirstOrDefault().Value;
        }

        public async ValueTask<object> GetOutputData(Guid id, string v)
        {
            var run = await GetState(id);

            var obj = GetOrCreateStateObject(v, run);

            await SaveState(id, run);

            return obj;
            var json = JsonConvert.SerializeObject(obj, _serializerSettings);


            return JsonConvert.DeserializeObject<JObject>(json, _serializerSettings);
        }

        public async ValueTask AddArrayItemAsync(IRunContext context, IWorkflow workflow, string key, IActionResult result)
        {

            var run = await GetOrCreateRun(context);


            var obj1 = GetOrCreateStateObject(key, run);
            obj1.UpdateFrom(result);

          

            await SaveState(context.RunId, run);

        }

        public async ValueTask AddArrayInput(IRunContext context, IWorkflow workflow, IAction action)
        {


            var run = await GetOrCreateRun(context);

            var obj = GetOrCreateStateObject(action.Key, run);
            obj.Input = action.Inputs;
           

            await SaveState(context.RunId, run);



        }
        public async ValueTask EndScope(IRunContext context, IWorkflow workflow, IAction action)
        {
            var run = await GetOrCreateRun(context);

            var obj = GetOrCreateStateObject(action.Key, run,true);

            var body = obj.Items;
            if (body == null)
                obj.Items = body = new List<Dictionary<string,ActionState>>();

            var actions = obj.Actions;
          //  actions.Parent.Remove(); 
            body.Add(actions);

            //Move current.actions into the array
             

            action.Index = body.Count;

            await SaveState(context.RunId, run);

        }

        public async ValueTask AddEvent(IRunContext context, IWorkflow workflow, IAction action, Event @event)
        {
            // Append events to events
            var run = await GetOrCreateRun(context);

            //if (run["events"] is not JArray events)
            //{
            //    run["events"] = events = new JArray();
            //}
            //events.Add(JToken.FromObject(@event));
            run.AddEvent(@event);

         


            await SaveState(context.RunId, run);
        }

        //public async ValueTask StartScope(IRunContext context, IWorkflow workflow, IAction action)
        //{
        //    var run = await GetOrCreateRun(context);

        //    var obj = GetOrCreateStateObject(action.Key, run);

        //    var body = obj.Items;
        //    if (body == null)
        //        obj.Items = body = new List<Dictionary<string, ActionState>>();

        //    var lastItem = JToken.FromObject(new { actions = new JObject() });

        //    body.Add(new ActionState());

        //    await SaveState(context.RunId, run);
        //}

        public async ValueTask AddInput(IRunContext context, IWorkflow workflow, IAction action)
        {
            var run = await GetOrCreateRun(context);

            var obj = GetOrCreateStateObject(action.Key, run);

            obj.Input = action.Inputs;
            obj.Type = action.Type;
        
          //  obj["input"] = JToken.FromObject(action.Inputs ?? new Dictionary<string, object>(), _serializer);
          //  obj["type"] = action.Type;
            //   obj.Merge(JToken.FromObject(new { input = action.Inputs }));

            await SaveState(context.RunId, run);
        }

        public void Dispose()
        {
           _scope.Dispose(); 
        }
    }
}
