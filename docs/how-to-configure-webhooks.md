# How to Configure Webhooks

Webhooks in RAFT are specially-crafted HTTP POST requests, usually with a JSON payload,
that are issued in response to an event in a RAFT service instance.   They are typically
received by an endpoint such as [Microsoft Logic Apps](https://azure.microsoft.com/en-us/services/logic-apps/)
 that in turn handles the event, which might take the form of sending an email, writing
to a collaboration channel like Teams or Slack, or auto-filing a bug.

Webhooks in RAFT are implemented as an
[Azure Event Grid Domain](https://docs.microsoft.com/en-us/azure/event-grid/event-domains). 

<br/>

## Viewing the Event Grid Domain

Looking at the resources created when you deploy the service you will see the Event Grid Domain with the name \<deploymentName\>-raft-events.

Looking at the Event Grid Domain you will see a list of "Domain Topics" at the bottom of the overview tab.

![](images/webhook_demo.jpg)

In this example the webhook name is "demo"

When you click on the domain name, you will see the events that have been defined for it. 

When a webhook is created a name is associated with one or more events. The name is the domain topic. Each event under the domain topic
can have it's own target URL which is called when the event is fired. The events which are currently supported are:

* JobStatus
* BugFound

The webhook name can be referenced in the [JobDefinition](../schema/jobdefinition.md) file in the webhook field. 

The webhook receiver must implement the [Endpoint validation protocol](https://docs.microsoft.com/en-us/azure/event-grid/webhook-event-delivery).
This allows the event grid to ensure that the target URL is a valid endpoint when it is established. Azure logic apps or Office 365
flow events implement this protocol for you.

<br/>

## How to Create a Webhook

To create a webhook in the CLI use the `webhook-create` parameter.

` python raft.py webhook-create <webhookName> --event [JobStatus | BugFound] --url "<url, be sure it's quoted>"`


<br/>

## How to Test a Webhook

Once you have created a webhook, you might like to test it in order to verify your receiver is working correctly.

To test a webhook in the CLI use the `webhook-test` parameter.

` python raft.py webhook-test <webhookName> --event [JobStatus | BugFound]`

Sending JobStatus dummy data via the test results in data that will look like this:

```
[{
        "id": "cef0cded-bd04-4fbe-a0a9-efb43c6564b7",
        "eventType": "JobStatus",
        "subject": "JobStatus",
        "data": {
            "tool": "RESTler",
            "jobId": "webhook-test-job-status-3878d99e-33fb-4d2a-9f9f-4e29fb939ab9",
            "state": "Running",
            "utcEventTime": "2020-10-05T22:29:47.973649Z",
            "details": ["WebhookV1-TestWebHook"],
            "agentName": "1",
            "isIdling": false
        },
        "dataVersion": "1.0",
        "metadataVersion": "1",
        "eventTime": "2020-10-05T22:29:48.08349Z",
        "topic": "/subscriptions/da3e8787-6e2b-4cae-b201-6e4cd71be189/resourceGroups/name-raft/providers/Microsoft.EventGrid/domains/name-raft-events/topics/demo"
    }
]
```

In this data structure the `data` portion of the structure is unique to the service. The other information is standardized from the Event Grid.

<br/>

## How to Incorporate Webhooks in a RAFT Job

TODO

<br/>

## How to Delete a Webhook

To delete a webhook in the CLI use the `webhook-delete` parameter.

```python
$ py raft.py webhook-delete <webhookName> --event [JobStatus | BugFound]`
```

It's required to provide the webhookName and the event you wish to delete. There is
not a way to delete the webhook name without specifing the event. Once the last event
has been deleted the domain topic is removed.
