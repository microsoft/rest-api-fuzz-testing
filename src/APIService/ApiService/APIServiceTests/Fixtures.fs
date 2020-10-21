// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
namespace Raft.TestFixtures

open Microsoft.Azure
open Microsoft.AspNetCore.Http
open Xunit
open NSubstitute
open Microsoft.Extensions.Configuration
open FSharp.Control.Tasks.V2
open Microsoft.Azure.Cosmos.Table
open Raft.StorageEntities
open Microsoft.Extensions.Logging
open Microsoft.ApplicationInsights
open Microsoft.ApplicationInsights.Extensibility

module Fixtures =

    let assertFailf format args =
        let msg = sprintf format args
        Assert.True(false, msg)

    let shouldContains actual expected = Assert.Contains(actual, expected) 
    let shouldEqual expected actual = Assert.Equal(expected, actual)
    let shouldNotNull expected = Assert.NotNull(expected)

    let createFakeContext () =
        Substitute.For<HttpContext>()

    let createFakeConfiguration (configurationValueToReturn : string) =
        let fakeConfiguration = Substitute.For<IConfiguration>()
        let fakeSection = Substitute.For<IConfigurationSection>()
        fakeSection.Value.ReturnsForAnyArgs(configurationValueToReturn) |> ignore
        fakeConfiguration.GetSection("").ReturnsForAnyArgs(fakeSection) |> ignore
        fakeConfiguration

    let createFakeMessageSender queue = 
        let fakeMessageSender = Substitute.For<ServiceBus.Core.IMessageSender>()
        fakeMessageSender.SendAsync(Microsoft.Azure.ServiceBus.Message()).Returns(task {return()}) |> ignore
        Raft.Utilities.serviceBusSenders <- Map.empty
                                               .Add(queue, fakeMessageSender)
        fakeMessageSender

    let createSerializerSettings() =
        let settings = Newtonsoft.Json.JsonSerializerSettings()
        settings.ContractResolver <- new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
        settings.Converters.Add(Newtonsoft.Json.Converters.StringEnumConverter())
        settings.Converters.Add(Microsoft.FSharpLu.Json.CompactUnionJsonConverter())
        settings.Formatting <- Newtonsoft.Json.Formatting.Indented
        settings.NullValueHandling <- Newtonsoft.Json.NullValueHandling.Ignore
        settings.DateTimeZoneHandling <- Newtonsoft.Json.DateTimeZoneHandling.Utc
        settings.TypeNameHandling <- Newtonsoft.Json.TypeNameHandling.Auto
        settings

    let createFakeRaftStorage (entity : JobStatusEntity option) =
        let fakeRaftStorage = Substitute.For<Raft.Interfaces.IRaftStorage>()
        let fakeQuery = TableQuery<JobStatusEntity>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, System.Guid.NewGuid().ToString()))  
        match entity with
        | Some e -> (fakeRaftStorage.GetJobStatusEntities fakeQuery).ReturnsForAnyArgs(task { return seq {e} }) |> ignore
        | None -> (fakeRaftStorage.GetJobStatusEntities fakeQuery).ReturnsForAnyArgs(task { return Seq.empty }) |> ignore
        fakeRaftStorage

    let createFakeRaftStorageJobEnity (entity : JobEntity option) =
        let fakeRaftStorage = Substitute.For<Raft.Interfaces.IRaftStorage>()
        match entity with
        | Some e -> (fakeRaftStorage.TableEntryExists Raft.StorageEntities.JobTableName "jobId" "jobId").ReturnsForAnyArgs(task { return Result.Ok() }) |> ignore
        | None -> (fakeRaftStorage.TableEntryExists Raft.StorageEntities.JobTableName "jobId" "jobId").ReturnsForAnyArgs(task { return Result.Error 400 }) |> ignore
        fakeRaftStorage

    let createFakeLogger<'T> = Substitute.For<ILogger<'T>>()
    let createFakeTelemetryClient = TelemetryClient(new TelemetryConfiguration(""))
