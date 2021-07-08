## REST API fuzz testing service and RESTler

The RAFT team owns and maintains the RESTler agent that runs on RAFT platform.
In order to provide a consistent platform for supporting multiple test tools there are some differences between how RESTler is configured when running using RAFT versus when running RESTler directly.

RAFT supports all of the same configuration values that RESTler supports.
RESTler uses a mix of camel case and underscore parameters that it accepts as part of it's run configurations. On the other hand RAFT only uses camel case parameters. For example RESTler uses parameter `restler_custom_payload` where the same parameter is called `restlerCustomPayload` in RAFT job definition. 

You can find the RESTler documentation at
https://github.com/microsoft/restler-fuzzer/tree/main/docs/user-guide

When using RESTler documentation for configuring RESTler tasks use the RAFT swagger definition for paramater name conversion.

The RAFT swagger definition can be found using this URL : *https://\<my-deployment\>-raft-apiservice.azurewebsites.net/swagger/index.html*


RAFT will do a DNS lookup on the host parameter, specified in the job definition file, 
on your behalf and fill in the IP parameter for you. RAFT also defaults the port number 
to 443 when using SSL and 80 when not using a secure connection. If for some reason you 
find that you still need to specify the IP and port number then manually provided values 
in TargetEndpoint configuration will override any lookup or default values.

## RESTler mode of operation

RESTler needs to compile the Swagger specifications into RESTler grammar as a first step. The output of the compile step is then consumed by any of the following steps: Test, TestFuzzLean, Fuzz.
To enable passing of data from one step to the next RAFT allows any file share in the storage account to be mounted by any task.
This way RAFT jobs can be executed in a "pipeline" manner by passing the output of the Compile job as input to the Test, TestFuzzLean, or Fuzz jobs.

A compile job produces a job ID and a file share is created and named using the job ID.
This makes it possible for you to take the output written to a file share of one job and mount it to use as input to another.
The diagram below illustrates this behavior in the job definition files.

![RESTler ](images/restler-configs-flow.png)
