using Newtonsoft.Json;
using WorkflowEngine.Core;

namespace EAVFW.Extensions.WorkflowEngine.Tests
{
    public class IdObjectMock
    {
        [JsonProperty("id")]
        public Guid Id { get; set; }
    }
    public class JournalizeFindResultMock
    {
        [JsonProperty("accountid")]
        public Guid AccountId { get; set; }
        [JsonProperty("formid")]
        public Guid FormId { get; set; }
        [JsonProperty("submissions")]
        public IdObjectMock[] Submissions { get; set; }
    }
    public class CreateESDHCaseFromFormSubmissionActionMock : IActionImplementation
    {
        public async ValueTask<object> ExecuteAsync(IRunContext context, IWorkflow workflow, IAction action)
        {
            var b = action.Inputs; ;

            return null;
        }
    }
    public class FindReportingFormSubmissionsToBeJournalizedFromTargetGroupsMock : IActionImplementation
    {
        public async ValueTask<object> ExecuteAsync(IRunContext context, IWorkflow workflow, IAction action)
        {
            return new[] {
                new JournalizeFindResultMock{
                    AccountId = Guid.Parse("00000000-0000-0000-0000-000000000000"),
                    FormId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                    Submissions = new []{ new IdObjectMock { Id = Guid.Parse("00000000-0000-0000-0000-000000000004") } }
            },
                   new JournalizeFindResultMock{
                    AccountId = Guid.Parse("00000000-0000-0000-0000-000000000004"),
                    FormId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                    Submissions = new []{ new IdObjectMock { Id = Guid.Parse("00000000-0000-0000-0000-000000000005") } }
            }
            }; 
        }
    }

    public class ScheduleBasedFormTargetGroupWorkflowMock : Workflow
    {
        public const string LoopOverSubmissionsActionname = "Loop_over_submissions";

        public ScheduleBasedFormTargetGroupWorkflowMock()
        {

            Id = Guid.Parse("083B2C72-825A-42B4-96A0-2CA9A8112113");
            Version = "1.0";
            Manifest = new WorkflowManifest
            {
                Triggers =
                {
                    ["Trigger"] = new TriggerMetadata
                    {
                        Type = "TimerTrigger",
                        Inputs =
                        {
                            ["cronExpression"] = "0 2 * * *",
                            //    ["runAtStartup"] = true
                        }
                    }
                },
                Actions =
                {
                    [nameof(FindReportingFormSubmissionsToBeJournalizedFromTargetGroupsMock)] = new ActionMetadata
                    {
                        Type = nameof(FindReportingFormSubmissionsToBeJournalizedFromTargetGroupsMock),
                        Inputs =
                        {

                        }
                    },
                    [LoopOverSubmissionsActionname] = new ForLoopActionMetadata
                    {
                        RunAfter = new WorkflowRunAfterActions
                        {
                            [nameof(FindReportingFormSubmissionsToBeJournalizedFromTargetGroupsMock)] = new []{"Succeded"}
                        },
                        ForEach =  $"@outputs('{nameof(FindReportingFormSubmissionsToBeJournalizedFromTargetGroupsMock)}')?['body']",
                        Type = "Foreach",
                        Inputs =
                        {

                        },
                        Actions =
                        {
                             [nameof(CreateESDHCaseFromFormSubmissionActionMock)] = new ActionMetadata
                            {
                                //RunAfter = new WorkflowRunAfterActions
                                //{
                                //    [nameof(FindFormSubmissionForAccountAction)] = new []{"Succeded"}
                                //},
                                Type = nameof(CreateESDHCaseFromFormSubmissionActionMock),
                                Inputs =
                                {
                                    ["accountid"] = $"@items('{LoopOverSubmissionsActionname}')?['accountid']",
                                    ["formid"] = $"@items('{LoopOverSubmissionsActionname}')?['formid']",
                                    ["submissions"] = $"@items('{LoopOverSubmissionsActionname}')?['submissions']"
                                }
                            }
                        }
                    }
                    }
            };



        }
    }

    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void TestMethod1()
        {
           //TOODO, how to mock hangfire
        }
    }
}