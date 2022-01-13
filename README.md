# REST API Fuzz Testing (RAFT)

<b><i>Dear RAFT Users,
<br>

Thank you for your usage of the RAFT fuzzing-as-a-service platform over the past year.  We have learned from many of you about your use cases for REST API Fuzzing, and these learnings will be applied to the future development of RESTler and related tooling at Microsoft.

Support for RAFT will end on January 28th.  The project will be archived, which means that the code will still be available for use, but will not be maintained.

Please email anlamb@microsoft.com prior to January 28th with any questions or concerns.

Thank you so much to all of the contributors and users of RAFT for being a part of our journey.
</i></b>

<br>
<br>



## A self hosted REST API Fuzzing-As-A-Service platform
RAFT enables painless fuzzing of REST API's using multiple fuzzers in parallel. Using a single command line
baked into your CI/CD pipeline developers can launch fuzz jobs against their services.

Following Swagger/OpenAPI tools are currently supported by RAFT

| Tool     | Description |
|----------|-------------|
| [RESTler](https://github.com/microsoft/restler-fuzzer) | RAFT has first class integration with this Microsoft Research tool - the first stateful fuzzing tool designed to automatically test your REST API's driven by your swagger/OpenApi specification. |
| [ZAP](https://www.zaproxy.org/) | RAFT supports Swagger/OpenAPI scanning functionality provided by ZAP|
| [Dredd](https://github.com/apiaryio/dredd) | RAFT supports Swagger/OpenAPI scanning functionality provided by Dredd|
| [Schemathesis](https://github.com/schemathesis/schemathesis) | RAFT supports Swagger/OpenAPI scanning functionality provided by Schemathesis|

##### RAFT key features
- Secret management via Azure Keyvault
- Webhook notifications: JobStatus (Job Created, Job Completed, Job Error) and BugFound for tools that produce bugs during run
- Ability to deploy RAFT jobs into a pre-provisioned Azure VNET
- Consistent Job Definition that works across all test tools
- Consistent Authentication mechanism for service under test across all test tools
- Long-term job results and logs storage via Azure Storage
- Ability to use the same RAFT job definitions locally using [Docker](https://www.docker.com) as in the Azure
- Ability to deploy dockerized service under test as well as any companion dockerized services part of a RAFT job definition for fully encapsulated testing

##### RAFT RESTler value add:
- Real Time RESTler fuzzing progress: HTTP Status code totals, number of bugs found
- Conversion of bugs found by RESTler to [Postman](https://www.postman.com) collections
- RESTler run definitions to Compile, Test and Fuzz in a single run definition
- Multi-step run definitions, to allow multiple parallel Test/Fuzz runs consume output of single Compile step
- Pre-populating RESTler fuzzing dictionary based on `mutationSeed`
- Avoid triggering BugFound events for bug hashes via `ignoreBugHashes` configured by user

As a platform, RAFT is designed to host any API fuzzers that are packaged into a docker container.
These can be configured and used in the system via configuration files and require no code changes to integrate.

### Getting Started
This project is designed to run on [Azure](https://azure.microsoft.com). See https://azure.com/free to create a free
subscription and receive $200 in credits. You can run this service (and much more!)
free for 30 days!

To deploy the service download the CLI release and run `python raft.py service deploy`. See
the [documentation](docs/how-to-deploy.md) for more details and the video tutorials linked below.

Once deployed, read about [how to submit a job](docs/how-to-submit-a-job.md) and
use the [samples](docs/samples.md) to try out the service and fuzzers!

### Documentation

* [Table of Contents](docs/index.md)
* [Overview](docs/how-it-works)
* [FAQ](docs/faq.md)
* [Video Tutorials](https://www.youtube.com/channel/UCUgE9Mv0GsavLg4I7z0lHVA)

### Swagger Documentation
Once the service is created, you can examine the REST interface of the service by browsing to the swagger page at https://\<deploymentName\>-raft-apiservice.azurewebsites.net/swagger

### Interesting in native code fuzzing?
Take a look at our sibling project [OneFuzz](https://github.com/microsoft/onefuzz)

### Microsoft Open Source Code of Conduct
https://opensource.microsoft.com/codeofconduct

### Trademarks
Trademarks This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft trademarks or logos is subject to and must follow Microsoft's Trademark & Brand Guidelines. Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship. Any use of third-party trademarks or logos are subject to those third-party's policies.

### Preferred Languages

We prefer all communications to be in English.
