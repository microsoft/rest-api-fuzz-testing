#### restler/running-60-tasks

This sample runs [RESTler](https://github.com/microsoft/restler-fuzzer) on [https://petstore.swagger.io](https://petstore.swagger.io) endpoint.

In this sample, the job description JSON will cause RAFT to execute sixty instances
of RESTler against raft.

This example shows how you can create multiple containers within one container group. Each container group
allows for up to 60 containers to run. In this sample the python script spawns pairs of `test` and `testfuzzlean` tasks.

This sample shows you how simple it is to create multiple tasks configured on the fly.

You can run the sample by executing `python run.py`.