using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using WorkflowEngine.Core;

namespace EAVFW.Extensions.WorkflowEngine.Models
{
    public enum WorkflowStateStatus
    {
        [EnumMember(Value = "Running")]
        Running = 10,
        [EnumMember(Value = "Succeded")]
        Succeded = 20,
        [EnumMember(Value = "Failed")]
        Failed = 30
    }
    public enum ActionStateStatus
    {
        [EnumMember(Value = "Running")]
        Running = 10,
        [EnumMember(Value = "Succeded")]
        Succeded = 20,
        [EnumMember(Value = "Failed")]
        Failed = 30
    }

    /// <summary>
    /// The workflow engine state of a workflow run
    /// </summary>
    public class WorkflowState
    {
        /// <summary>
        /// The status for the run
        /// </summary>
        [JsonProperty("status")]
        [JsonConverter(typeof(StringEnumConverter))]

        public WorkflowStateStatus Status { get; set; }

        /// <summary>
        /// The uniuque identifier of the principal that runs the workflow
        /// </summary>
        [JsonProperty("principal")]
        public string Principal { get; set; }

        /// <summary>
        /// The state of each actions part of this workflow
        /// </summary>
        [JsonProperty("actions")]
        public Dictionary<string, ActionState> Actions { get; set; } = new Dictionary<string, ActionState>();

        /// <summary>
        /// The triggers for this run
        /// </summary>
        [JsonProperty("triggers")]
        public Dictionary<string, object> Triggers { get; set; } = new Dictionary<string, object>();
        /// <summary>
        /// The list of events from this workflow run
        /// </summary>
        [JsonProperty("events")]
        public ICollection<Event> Events { get; set; } = new List<Event>();

        /// <summary>
        /// The output of this workflow run
        /// </summary>
        [JsonProperty("body")]
        public object Body { get; set; }

        [JsonProperty("failedReason")]
        public string FailedReason { get; set; }

        public void AddEvent(Event @event)
        {
            Events.Add(@event);

            if (@event is IHaveFinisningStatus finisningStatus)
            {
                Status = Enum.Parse<WorkflowStateStatus>(finisningStatus.Result.Status);

                Body = finisningStatus.Result.Result;
                if (Status == WorkflowStateStatus.Failed)
                    FailedReason = finisningStatus.Result.FailedReason;


            }
        }
    }


    public class ActionState
    {
        [JsonProperty("input")]
        public object Input { get; set; }
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("status")]
        [JsonConverter(typeof(StringEnumConverter))]

        public ActionStateStatus Status { get; set; }

        /// <summary>
        /// The output of this action run
        /// </summary>
        [JsonProperty("body")]
        public object Body { get; set; }

        [JsonProperty("failedReason")]
        public string FailedReason { get; set; }

        /// <summary>
        /// The state of each actions part of this action
        /// </summary>
        [JsonProperty("actions")]
        public Dictionary<string, ActionState> Actions { get; set; } = new Dictionary<string, ActionState>();


        [JsonProperty("items")]
        public ICollection<Dictionary<string,ActionState>> Items { get; set; }

        public void ParseStatus(string status)
        {
            Status = Enum.Parse<ActionStateStatus>(status); 
        }

        public void UpdateFrom(IActionResult result)
        {
            ParseStatus(result.Status);
            Body = result.Result;
            FailedReason = result.FailedReason;
             
        }
    }
}
