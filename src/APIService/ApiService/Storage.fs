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
                    let! createResult = table.CreateIfNotExistsAsync() |> Async.AwaitTask
                    return table
                }
                            
            member this.GetJobStatusEntities (query : TableQuery<JobStatusEntity>) =
                task {
                    let me = this :> IRaftStorage
                    let! table = me.GetTable StorageEntities.JobStatusTableName
                    //https://github.com/rspeele/TaskBuilder.fs
                    // TODO: Remove tail recursion use of the task
                    // See github readme
                    
                    let rec doExecute (token: TableContinuationToken) (results: JobStatusEntity seq) =
                        task {
                            let! segment = table.ExecuteQuerySegmentedAsync(query, token) 
                        
                            if segment.ContinuationToken = null then
                                return Seq.append results segment.Results
                            else
                                return! doExecute segment.ContinuationToken (Seq.append results segment.Results)
                        }
                    return! doExecute null []
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

