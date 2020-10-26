// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace JobControllerTests

open Xunit
open System.IO
open Raft.Controllers
open Raft.TestFixtures
open Raft.Errors

[<CollectionAttribute("unit")>]
type jobsPOSTTests() = 

    [<Fact>]
    member this.``POST /jobs compile job`` () =
        async {
            let fakeMessageSender = Fixtures.createFakeMessageSender Raft.Message.ServiceBus.Queue.create
            Raft.Utilities.raftStorage <- Fixtures.createFakeRaftStorage None
            Raft.Utilities.toolsSchemas <- Map.empty.Add("RESTler", None)

            let contents = File.ReadAllText("grade-track-restler-compile.json")
            let compileJobDefinition = Newtonsoft.Json.JsonConvert.DeserializeObject<DTOs.JobDefinition>(contents, Fixtures.createSerializerSettings())

            let jobController = jobsController(Fixtures.createFakeTelemetryClient, Fixtures.createFakeLogger<jobsController>)
            jobController.ControllerContext.HttpContext <- Fixtures.createFakeContext()

            let! jobResult = jobController.Post("", compileJobDefinition) |> Async.AwaitTask
            let result = jobResult.Value :?> DTOs.JobResponse
            Assert.True(result.JobId.StartsWith("grade-track-compile-"))

            let parseResult, jobGuid = System.Guid.TryParse(result.JobId.Substring("grade-track-compile-".Length))
            Assert.True(parseResult)
        }

    [<Fact>]
    member this.``POST /jobs fuzz job`` () =
        async {
            let fakeMessageSender = Fixtures.createFakeMessageSender Raft.Message.ServiceBus.Queue.create
            Raft.Utilities.raftStorage <- Fixtures.createFakeRaftStorage None
            Raft.Utilities.toolsSchemas <- Map.empty.Add("RESTler", None)

            let contents = File.ReadAllText("grade-track-restler-fuzz.json")
            let fuzzJobDefinition = Newtonsoft.Json.JsonConvert.DeserializeObject<DTOs.JobDefinition>(contents, Fixtures.createSerializerSettings())

            let jobController = jobsController(Fixtures.createFakeTelemetryClient, Fixtures.createFakeLogger<jobsController>)
            jobController.ControllerContext.HttpContext <- Fixtures.createFakeContext()

            let! jobResult = jobController.Post("", fuzzJobDefinition) |> Async.AwaitTask
            let result = jobResult.Value :?> DTOs.JobResponse

            let parseResult, jobGuid = System.Guid.TryParse(result.JobId)
            Assert.True(parseResult)
        }

    [<Fact>]
    member this.``POST /jobs compile job with empty payload`` () =
        async {
            let fakeMessageSender = Fixtures.createFakeMessageSender Raft.Message.ServiceBus.Queue.create
            Raft.Utilities.raftStorage <- Fixtures.createFakeRaftStorage None

            let contents = File.ReadAllText("grade-track-restler-compile.json")
            let compileJobDefinition = Newtonsoft.Json.JsonConvert.DeserializeObject<DTOs.JobDefinition>(contents, Fixtures.createSerializerSettings())
            let compileJobDefinition =
                {
                    compileJobDefinition with
                        Tasks = Array.empty
                }

            let jobController = jobsController(Fixtures.createFakeTelemetryClient, Fixtures.createFakeLogger<jobsController>)
            jobController.ControllerContext.HttpContext <- Fixtures.createFakeContext()

            let! failedResult = jobController.Post("", compileJobDefinition) |> Async.AwaitTask
            let errorResult = failedResult.Value :?> ApiError
            Assert.True(errorResult.Error.Code = ApiErrorCode.ParseError)
        }