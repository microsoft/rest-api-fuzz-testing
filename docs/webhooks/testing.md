# Testing a webhook

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