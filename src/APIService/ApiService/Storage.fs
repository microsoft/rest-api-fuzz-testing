// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Raft

open System
open Raft.StorageEntities
open Microsoft.AspNetCore.Http
open Raft.Controllers
open Microsoft.Azure.Cosmos.Table
open Raft.Interfaces
open FSharp.Control.Tasks.V2.ContextSensitive
open Microsoft.Extensions.Logging

module Storage =
    type RaftStorage (connectionString : string) =
        interface IRaftStorage  with
            member this.IsSuccessCode statusCode =
                statusCode >= 200 && statusCode <= 299

            member this.GetTable tableName = 
                async {
                    let storageAccount = CloudStorageAccount.Parse(connectionString)
                    let tableClient = storageAccount.CreateCloudTableClient()
                    let table = tableClient.GetTableReference(tableName)
                    return table
                }

            member this.CreateTable tableName  = 
                async {
                    let storageAccount = CloudStorageAccount.Parse(connectionString)
                    let tableClient = storageAccount.CreateCloudTableClient()
                    let table = tableClient.GetTableReference(tableName)
                    let! _ = table.CreateIfNotExistsAsync() |> Async.AwaitTask
                    return table
                }

            member this.GetJobStatusEntities (query : TableQuery<JobStatusEntity>) =
                task {
                    let me = this :> IRaftStorage
                    let! table = me.GetTable StorageEntities.JobStatusTableName
                    
                    let! segment = table.ExecuteQuerySegmentedAsync(query, null)
                    let results = ResizeArray(segment.Results)
                    
                    let mutable token = segment.ContinuationToken |> Option.ofObj
                    while token.IsSome do
                        let! segment = table.ExecuteQuerySegmentedAsync(query, token.Value)
                        token <- segment.ContinuationToken |> Option.ofObj
                        results.AddRange(segment.Results)
                    return results |> Seq.cast
                }

            member this.TableEntryExists tableName partitionKey rowKey =
                task{
                    let me = this :> IRaftStorage
                    let! table = me.GetTable tableName
                    let getOp = Microsoft.Azure.Cosmos.Table.TableOperation.Retrieve(partitionKey, rowKey)
                    let! getResult = table.ExecuteAsync(getOp) |> Async.AwaitTask
                    if (me.IsSuccessCode getResult.HttpStatusCode) then
                        return Result.Ok()
                    else
                        return Result.Error getResult.HttpStatusCode

                }

            member this.InsertEntity tableName entity =
                task {
                    let me = this :> IRaftStorage
                    let! table = me.GetTable tableName
                    let insertOperation = TableOperation.Insert(entity)
                    let! insertOperationResult = table.ExecuteAsync(insertOperation) |> Async.AwaitTask
                    if me.IsSuccessCode insertOperationResult.HttpStatusCode then
                        return Result.Ok()
                    else
                        return Result.Error insertOperationResult.HttpStatusCode
                }

