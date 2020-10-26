// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace JobControllerTests

open Xunit
open System.IO
open Raft.Controllers
open Raft.TestFixtures
open Raft.Errors
open Raft.StorageEntities

[<CollectionAttribute("unit")>]
type jobsRePOSTTests() = 

    [<Fact>]
    member this.``Re-POST /jobs compile job`` () =
        async {
            let fakeMessageSender = Fixtures.createFakeMessageSender Raft.Message.ServiceBus.Queue.create
            let jobId = System.Guid.NewGuid().ToString()

            let jobStatusJson = File.ReadAllText("job-status.json")
            let entity = JobStatusEntity(jobId, jobId, jobStatusJson)
            Raft.Utilities.raftStorage <- Fixtures.createFakeRaftStorage (Some entity)
            Raft.Utilities.toolsSchemas <- Map.empty.Add("RESTler", None)

            let contents = File.ReadAllText("grade-track-restler-compile.json")
            let compileJobDefinition = Newtonsoft.Json.JsonConvert.DeserializeObject<DTOs.JobDefinition>(contents, Fixtures.createSerializerSettings())

            let jobController = jobsController(Fixtures.createFakeTelemetryClient, Fixtures.createFakeLogger<jobsController>)
            jobController.ControllerContext.HttpContext <- Fixtures.createFakeContext()
            let! jobResult = jobController.RePost(jobId, compileJobDefinition) |> Async.AwaitTask

            let rePostResult = jobResult.Value :?> DTOs.JobResponse
            Assert.True(rePostResult.JobId = jobId )
        }

    [<Fact>]
    member this.``Re-POST /jobs fail because jobId does not exist`` () =
        async {
            let fakeMessageSender = Fixtures.createFakeMessageSender Raft.Message.ServiceBus.Queue.create
            Raft.Utilities.raftStorage <- Fixtures.createFakeRaftStorageJobEnity None
            Raft.Utilities.toolsSchemas <- Map.empty.Add("RESTler", None)

            let contents = File.ReadAllText("grade-track-restler-compile.json")
            let compileJobDefinition = Newtonsoft.Json.JsonConvert.DeserializeObject<DTOs.JobDefinition>(contents, Fixtures.createSerializerSettings())

            let jobController = jobsController(Fixtures.createFakeTelemetryClient, Fixtures.createFakeLogger<jobsController>)
            jobController.ControllerContext.HttpContext <- Fixtures.createFakeContext()

            let! failedResult = jobController.RePost("29211868-8178-4e81-9b8d-d52025b4c2d4", compileJobDefinition) |> Async.AwaitTask
            let errorResult = failedResult.Value :?> ApiError
            Assert.True(errorResult.Error.Code = ApiErrorCode.NotFound)
        }

        