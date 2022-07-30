
using DotNetDevOps.Extensions.EAVFramework;
using DotNetDevOps.Extensions.EAVFramework.Endpoints;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using WorkflowEngine.Core;

namespace EAVFW.Extensions.WorkflowEngine
{
    public class EAVFWOutputsRepository<TContext, WorkflowRun> : IOutputsRepository
        where TContext: DynamicContext
        where WorkflowRun : DynamicEntity, IWorkflowRun, new ()
    {
        private readonly EAVDBContext<TContext> _eAVDBContext;
        protected ClaimsPrincipal Principal { get; }
        // public ConcurrentDictionary<Guid, JToken> Runs { get; set; } = new ConcurrentDictionary<Guid, JToken>();
        public EAVFWOutputsRepository(EAVDBContext<TContext> eAVDBContext, IOptions<EAVFWOutputsRepositoryOptions> options)
        {
            _eAVDBContext = eAVDBContext;
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
            JToken run = await GetOrCreateRun(context);

            var obj = GetOrCreateStateObject(action.Key, run);

            obj.Merge(JToken.FromObject(new { status = result.Status, body = result.Result, failedReason = result.FailedReason }));


            await SaveState(context.RunId, run);

        }

        private static JObject GetOrCreateStateObject(string key, JToken run)
        {
            JToken actions = run["actions"];

            var idx = key.IndexOf('.');
            while (idx != -1)
            {
                actions = actions[key.Substring(0, idx)];
                key = key.Substring(idx + 1);
                idx = key.IndexOf('.');


                actions = actions["actions"] ?? (actions["actions"] = new JObject());
            }

            JObject obj = actions[key] as JObject;
            if (obj == null)
            {
                actions[key] = obj = new JObject();
            }

            return obj;
        }
        private async Task SaveState(Guid runId, JToken state)
        {
            using var ms = new MemoryStream();
            using var tinyStream = new GZipStream(ms, CompressionMode.Compress);
            using var writer = new JsonTextWriter(new StreamWriter(tinyStream));
            state.WriteTo(writer);
            writer.Flush();
            tinyStream.Flush();

            var run = await _eAVDBContext.Set<WorkflowRun>().FindAsync(runId);

            run.State = ms.ToArray();

            _eAVDBContext.Set<WorkflowRun>().Update(run);
            await _eAVDBContext.SaveChangesAsync(Principal);
        }

        public async Task<JToken> GetState(Guid runId)
        {
            var run = await _eAVDBContext.Set<WorkflowRun>().FindAsync(runId);
            using var tinyStream = new JsonTextReader(
                new StreamReader(new GZipStream(new MemoryStream(run.State), CompressionMode.Decompress)));

            var serializer = JsonSerializer.CreateDefault();
            return serializer.Deserialize<JObject>(tinyStream);
        }

        private async Task<JToken> GetOrCreateRun(IRunContext context)
        {
            var run = await _eAVDBContext.Set<WorkflowRun>().FindAsync(context.RunId);
            if (run == null)
            {
                var data = new JObject(new JProperty("actions", new JObject()), new JProperty("triggers", new JObject()));

                using var ms = new MemoryStream();
                using var tinyStream = new GZipStream(ms, CompressionMode.Compress);
                using var writer = new JsonTextWriter(new StreamWriter(tinyStream));
                data.WriteTo(writer);
                writer.Flush();
                tinyStream.Flush();


                var dataarr = ms.ToArray();

                run = new WorkflowRun() { Id = context.RunId, State = dataarr };
                _eAVDBContext.Set<WorkflowRun>().Add(run);
                await _eAVDBContext.SaveChangesAsync(Principal);

                return data;
            }

            {

                using var tinyStream = new JsonTextReader(new StreamReader(new GZipStream(new MemoryStream(run.State), CompressionMode.Decompress)));

                var serializer = JsonSerializer.CreateDefault();
                return serializer.Deserialize<JObject>(tinyStream);


            }

            // return Runs.GetOrAdd(context.RunId, (id) => new JObject(new JProperty("actions", new JObject()), new JProperty("triggers", new JObject())));
        }

        public async ValueTask AddAsync(IRunContext context, IWorkflow workflow, ITrigger trigger)
        {
            JToken run = await GetOrCreateRun(context);

            run["triggers"][trigger.Key] = JToken.FromObject(new { time = trigger.ScheduledTime, body = trigger.Inputs });

            await SaveState(context.RunId, run);
        }


        public async ValueTask<object> GetTriggerData(Guid id)
        {
            var run = await GetState(id);
            return run["triggers"].OfType<JProperty>().FirstOrDefault().Value;
        }

        public async ValueTask<object> GetOutputData(Guid id, string v)
        {
            var run = await GetState(id);

            var obj = GetOrCreateStateObject(v, run);

            await SaveState(id, run);

            var json = JsonConvert.SerializeObject(obj);


            return JsonConvert.DeserializeObject<JObject>(json);
        }

        public async ValueTask AddArrayItemAsync(IRunContext context, IWorkflow workflow, string key, IActionResult result)
        {

            JToken run = await GetOrCreateRun(context);


            var obj1 = GetOrCreateStateObject(key, run);

            obj1.Merge(JToken.FromObject(new { status = result.Status, body = result.Result, failedReason = result.FailedReason }));

            await SaveState(context.RunId, run);
            //  var obj = GetOrCreateStateObject(key.Substring(0, key.LastIndexOf('.')), run);

            //  var body = obj["items"] as JArray;
            //  if (body==null)
            //      obj["items"] =body= new JArray();

            //  var lastItem = body[body.Count-1] as JObject;
            //  var actions = lastItem["actions"] as JObject;

            //  var itteration = actions[key.Substring(key.LastIndexOf('.')+1)] as JObject;
            // // if (itteration==null)
            ////      actions[key.Substring(key.LastIndexOf('.')+1)] = itteration = new JObject();

            //  itteration.Merge(JToken.FromObject(JToken.FromObject(new { status = result.Status, body = result.Result, failedReason = result.FailedReason })));




            // return new ValueTask();
        }

        public async ValueTask AddArrayInput(IRunContext context, IWorkflow workflow, IAction action)
        {


            JToken run = await GetOrCreateRun(context);

            var obj = GetOrCreateStateObject(action.Key, run);

            obj.Merge(JToken.FromObject(new { input = action.Inputs }));

            await SaveState(context.RunId, run);
            //var obj = GetOrCreateStateObject(action.Key.Substring(0, action.Key.LastIndexOf('.')), run);


            //var body = obj["items"] as JArray;

            //var lastItem = body[body.Count-1] as JObject;
            //var actions = lastItem["actions"] as JObject;

            //actions[action.Key.Substring(action.Key.LastIndexOf('.')+1)] = JToken.FromObject(new { input = action.Inputs });


            // return new ValueTask();


        }
        public async ValueTask EndScope(IRunContext context, IWorkflow workflow, IAction action)
        {
            JToken run = await GetOrCreateRun(context);

            var obj = GetOrCreateStateObject(action.Key, run);

            var body = obj["items"] as JArray;
            if (body == null)
                obj["items"] = body = new JArray();

            var actions = obj["actions"];
            actions.Parent.Remove();
            body.Add(actions);


            action.Index = body.Count;

            await SaveState(context.RunId, run);

        }
        public async ValueTask StartScope(IRunContext context, IWorkflow workflow, IAction action)
        {
            JToken run = await GetOrCreateRun(context);

            var obj = GetOrCreateStateObject(action.Key, run);

            var body = obj["items"] as JArray;
            if (body == null)
                obj["items"] = body = new JArray();

            var lastItem = JToken.FromObject(new { actions = new JObject() });

            body.Add(lastItem);

            await SaveState(context.RunId, run);
        }

        public async ValueTask AddInput(IRunContext context, IWorkflow workflow, IAction action)
        {
            JToken run = await GetOrCreateRun(context);

            var obj = GetOrCreateStateObject(action.Key, run);

            obj.Merge(JToken.FromObject(new { input = action.Inputs }));

            await SaveState(context.RunId, run);
        }



    }
}
