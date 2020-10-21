# Overview  

Job configurations are submitted to the RAFT service front-end.
A message is put onto the service bus and processed by the back-end orchestrator.
Once the message is received an Azure storage File Share is created with the job Id as the share name.
The orchestrator creates a container group for each job. The container group name is the job Id and you can see it in the portal in the resource group where the service was deployed. 
Each task runs as a container within the container group.

The file share is mounted to each container in the container group as the "working directory" where the running tool should write all its results.
When an agent processes the task output, it may send job progress events on the service bus. These events are recorded in the job status azure table by job id. 
Status can be retrieved with the CLI or with a REST API call to the RAFT service.