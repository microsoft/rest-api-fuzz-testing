#### restler/no-authentication

This sample runs [RESTler](https://github.com/microsoft/restler-fuzzer) on [https://petstore.swagger.io](https://petstore.swagger.io) endpoint.

RESTler requires a compile step before running a Test or Fuzz step. In the **compile.json** file
notice the **namePrefix** field, this prefix will be prepended to the job id which can make it easier to find
the job id on the resulting file share. 

Also notice the **toolConfiguration** section. This section defines tool specific information. Here the information
tells RESTler to perform the `Compile` task. 

In the **fuzz.json** file notice that there are two tasks defined. These tasks run in parallel. The fuzz
task has a duration defined which means the task will run no longer than this duration. The `test-fuzz-lean` task, 
or any RESTler `test` task, should not define a duration.

You can run the sample by executing `python run.py`. This sample creates three jobs: Compile, Fuzz, Replay. The script pipes output of Compile job into Fuzz by mounting a output file-share from Compile step as an input read-only file-share to Fuzz step. Then Fuzz step output file-share is mounted are read-only file-share to Replay step.

Each step will print Results URL to the console - this is a URL that you can paste in a web browser to access Azure file share produced by the job run. For Compile step the results file-share  will contain RESTler compile step output. For Fuzz step the results file-share contains all RESTler output plus all bugs found by the job. For Replay step file-share contains logs after reproducing all the bugs found by Fuzz step.



