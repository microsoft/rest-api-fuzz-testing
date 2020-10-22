// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace JobControllerTests

open Xunit
open System.IO
open Raft.Controllers
open Raft.TestFixtures
open Raft.Errors

[<CollectionAttribute("unit")>]
type jobsRePOSTTests() = 

    [<Fact>]
    member this.``Re-POST /jobs compile job`` () =
        async {
            let fakeMessageSender = Fixtures.createFakeMessageSender Raft.Message.ServiceBus.Queue.create
            Raft.Utilities.raftStorage <- Fixtures.createFakeRaftStorage None

            let contents = File.ReadAllText("grade-track-restler-compile.json")
            let compileJobDefinition = Newtonsoft.Json.JsonConvert.DeserializeObject<DTOs.JobDefinition>(contents, Fixtures.createSerializerSettings())

            let jobController = jobsController(Fixtures.createFakeTelemetryClient, Fixtures.createFakeLogger<jobsController>)
            jobController.ControllerContext.HttpContext <- Fixtures.createFakeContext()

            let! jobResult = jobController.RePost("29211868-8178-4e81-9b8d-d52025b4c2d4", compileJobDefinition) |> Async.AwaitTask
            let rePostResult = jobResult.Value :?> DTOs.JobResponse

            let parseResult, jobGuid = System.Guid.TryParse(rePostResult.JobId)
            Assert.True(parseResult)
            Assert.True(jobGuid.ToString() = "29211868-8178-4e81-9b8d-d52025b4c2d4" )
        }

    [<Fact>]
    member this.``Re-POST /jobs fail because jobId does not exist`` () =
        async {
            let fakeMessageSender = Fixtures.createFakeMessageSender Raft.Message.ServiceBus.Queue.create
            Raft.Utilities.raftStorage <- Fixtures.createFakeRaftStorageJobEnity None

            let contents = File.ReadAllText("grade-track-restler-compile.json")
            let compileJobDefinition = Newtonsoft.Json.JsonConvert.DeserializeObject<DTOs.JobDefinition>(contents, Fixtures.createSerializerSettings())

            let jobController = jobsController(Fixtures.createFakeTelemetryClient, Fixtures.createFakeLogger<jobsController>)
            jobController.ControllerContext.HttpContext <- Fixtures.createFakeContext()

            let! failedResult = jobController.RePost("29211868-8178-4e81-9b8d-d52025b4c2d4", compileJobDefinition) |> Async.AwaitTask
            let errorResult = failedResult.Value :?> ApiError
            Assert.True(errorResult.Error.Code = ApiErrorCode.NotFound)
        }

        