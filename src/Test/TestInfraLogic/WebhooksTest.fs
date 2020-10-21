namespace TestInfraLogic
open Microsoft.Extensions.Logging
open Microsoft.Azure.Cosmos.Table
open System.Net

module WebhooksTest = 
    open Newtonsoft.Json

    type ValidationData =
        {
            ValidationCode : string
        }

    type Validation =
        {
            Data : ValidationData
        }

    type ValidationResponse =
        {
            ValidationResponse : string
        }

    (*
[
  {
    "id": "2d1781af-3a4c-4d7c-bd0c-e34b19da4e66",
    "topic": "/subscriptions/xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    "subject": "",
    "data": {
      "validationCode": "512d38b6-c7b8-40c8-89fe-f46f9e9622b6",
      "validationUrl": "https://rp-eastus2.eventgrid.azure.net:553/eventsubscriptions/estest/validate?id=512d38b6-c7b8-40c8-89fe-f46f9e9622b6&t=2018-04-26T20:30:54.4538837Z&apiVersion=2018-05-01-preview&token=1A1A1A1A"
    },
    "eventType": "Microsoft.EventGrid.SubscriptionValidationEvent",
    "eventTime": "2018-01-25T22:12:19.4556811Z",
    "metadataVersion": "1",
    "dataVersion": "1"
  }
]    

must return:
{
"validationResponse": "512d38b6-c7b8-40c8-89fe-f46f9e9622b6"
}
*)
    type TupleAsArraySettings =
        static member formatting = Newtonsoft.Json.Formatting.Indented
        static member settings =
            JsonSerializerSettings(
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                Converters = [| Microsoft.FSharpLu.Json.CompactUnionJsonConverter(true, true) |]
            )
    type private J = Microsoft.FSharpLu.Json.With<TupleAsArraySettings>

    let getWebHooksTestTable connectionString  = 
        async {
            let TableName = "WebhooksTriggerTest"
            let storageAccount = CloudStorageAccount.Parse(connectionString)
            let tableClient = storageAccount.CreateCloudTableClient()
            let table = tableClient.GetTableReference(TableName)
            return table
        }

    let validation (logger: ILogger) (msg: string) =
        async {
            match J.tryDeserialize msg with
            | Choice1Of2 (v: Validation list) when not(List.isEmpty v) ->
                match (List.head v).Data.ValidationCode with
                | null -> return false, Unchecked.defaultof<_>
                | code ->
                    logger.LogInformation(sprintf "Got Webhooks Validation message: %s" msg)
                    return true, {ValidationResponse = code}
            | Choice1Of2 _
            | Choice2Of2 _ -> 
                return false, Unchecked.defaultof<_>

        } |> Async.StartAsTask



    type WebhookData =
        {
            JobId : string
        }

    type Webhook =
        {
            Subject : string
            Id : string
            Data : WebhookData
            EventType: string
        }

    type WebhoookTestRecordEntity(jobId, webhookId, message) = 
        inherit TableEntity(partitionKey=jobId, rowKey=webhookId)
        new() = WebhoookTestRecordEntity(null, null, null)
        member val WebhoookTestRecordEntity : string = message with get, set

    let post (logger: ILogger, storageTableConnectionString: string) (msg: string) =
        async {
            logger.LogInformation(sprintf "Got Webhooks Test POST message: %s" msg)

            match J.tryDeserialize msg with
            | Choice1Of2(webhookEvents: Webhook list) ->
                if List.length webhookEvents <> 1 then
                    logger.LogWarning (sprintf "Expected only one webhook event, but got %d in %s"  (List.length webhookEvents) msg)

                for event in webhookEvents do
                    if event.EventType = "BugFound" || event.EventType = "JobStatus" then
                        let! table = getWebHooksTestTable(storageTableConnectionString)

                        let entity = WebhoookTestRecordEntity(event.Data.JobId, event.Id, msg)

                        let insertOp = TableOperation.InsertOrReplace(entity)
                        let! insertResult = table.ExecuteAsync(insertOp) |> Async.AwaitTask

                        if insertResult.HttpStatusCode = int HttpStatusCode.OK || insertResult.HttpStatusCode = int HttpStatusCode.NoContent then
                            ()
                        else
                            let errMsg = sprintf "Failed to insert : %s into webhooks test table with error code : %A" msg insertResult.HttpStatusCode
                            logger.LogError errMsg
                    else 
                        logger.LogInformation (sprintf "Ignoring webhook test event, since it is of unsupported event type : %s" event.EventType)
                return ()
            | Choice2Of2 err -> 
                logger.LogWarning (sprintf "Failed to deserialize %s due to %s" msg err)
                return ()

        } |> Async.StartAsTask

    let get (logger: ILogger, storageTableConnectionString: string) (jobId: string) =
        async {
            logger.LogInformation(sprintf "Got Webhooks Test GET for job id : %A" jobId)
            let! table = getWebHooksTestTable(storageTableConnectionString)
            let query = TableQuery<WebhoookTestRecordEntity>().Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, jobId.ToString()))
            
            let rec getResults(results: ResizeArray<_>) =
                async {
                    let! segment = table.ExecuteQuerySegmentedAsync(query, null) |> Async.AwaitTask
                    if segment = null then
                        return results
                    else
                        results.AddRange(segment.Results)
                        if segment.ContinuationToken = null then
                            return results
                        else
                            return! getResults results
                }
            let! results = getResults (ResizeArray())
            let results = results |> Seq.map (fun r -> r.WebhoookTestRecordEntity)

            logger.LogInformation (sprintf "Retrieved Webhook Test Results for jobId %s : %A" jobId results)
            return results
        } |> Async.StartAsTask

