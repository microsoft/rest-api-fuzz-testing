// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Raft
open System
open Raft.StorageEntities
open Microsoft.Azure.Cosmos.Table
open System.Threading.Tasks

module Interfaces =

    type IRaftStorage = 
        abstract member IsSuccessCode : int -> bool
        abstract member CreateTable : string -> Async<CloudTable>
        abstract member GetTable : string -> Async<CloudTable>
        abstract member GetJobStatusEntities : TableQuery<JobStatusEntity> -> Task<seq<JobStatusEntity>>
        abstract member TableEntryExists : string -> string -> string -> Task<Result<unit,int>>
        abstract member InsertEntity : string -> TableEntity -> Task<Result<unit,int>>
