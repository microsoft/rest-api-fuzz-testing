# Webhooks overview

Webhooks are managed in the system by an [Azure Event Grid Domain](https://docs.microsoft.com/en-us/azure/event-grid/event-domains). 

Looking at the resources created when you deploy the service you will see the Event Grid Domain with the name \<deploymentName\>-raft-events.

Looking at the Event Grid Domain you will see a list of "Domain Topics" at the bottom of the overview tab.

![](../images/webhook_demo.jpg)

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