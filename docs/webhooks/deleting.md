# Deleting a webhook

To delete a webhook in the CLI use the `webhook-delete` parameter.

` python raft.py webhook-delete <webhookName> --event [JobStatus | BugFound]`

It's required to provide the webhookName and the event you wish to delete. There is not a way to delete the webhook name without specifing the event. Once the last event has been deleted the domain topic is removed.
