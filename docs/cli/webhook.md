# Webhook Commands</br>

These commands are used to create and control webhooks. Webhooks are implemented by using an [EventDomain](https://docs.microsoft.com/en-us/azure/event-grid/event-domains)

## webhook-events</br>

Lists the set of events which will generate webhooks.

## webhook-create \<name\> --event \<eventName\> --url \<targetUrl\></br>

Creates a webhook. The \<name\> value is a string that is used in your jobDefinition file.
The --event \<eventName\> parameter is required. The event name must be one of the values returned from webhook-events.
The --url \<targetUrl\> parameter is required. The targetUrl will receive the webhook. The targetUrl must implement endpoint validation. See https://docs.microsoft.com/en-us/azure/event-grid/webhook-event-delivery
A common and simple use as the target is an azure logic app. This provides a simple way to process your webhooks posting to teams or slack, creating new work items, etc.

## webhook-test \<name\> --event \<eventName\></br>

Test your webhook receiver. Dummy data will be sent to the webhook endpoint you are testing.
The **--event** parameter is required.

## webhook-list \<name\></br>

List the definition of webhook \<name\>.
Use of **--event** parameter is optional to limit what is returned.

## webhook-delete \<name\> --event \<eventName\></br>

Deletes a webhook for a specific event.
The **--event** parameter is required.