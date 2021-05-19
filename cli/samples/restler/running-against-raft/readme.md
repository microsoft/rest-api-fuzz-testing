#### restler/running-against-raft

This sample runs [RESTler](https://github.com/microsoft/restler-fuzzer) on your RAFT deployment.

In this sample, the job description JSON will cause RAFT to execute RESTler against the
RAFT service itself using authentication. 

You will notice in the **compile.json** job definition values in the `customerPayload` dictionary. This
dictionary is given to RESTler and it's values are used as fuzzing values in the REST calls.

You can run the sample in your Azure RAFT deployment by executing `python run.py`. 
You can run the sample in your local docker service by executing `python run.py --local`. You still have to have Azure RAFT service deployed, since this is service under test.
This sample creates three jobs: Compile, Test and Fuzz. 
The script pipes output of Compile job into Test and then into Fuzz jobs by mounting a output file-share from Compile step as an input read-only file-share to Fuzz step. 

Each step will print Results URL to the console - this is a URL that you can paste in a web browser to access Azure file share produced by the job run. For Compile step the results file-share  will contain RESTler compile step output. For Fuzz step the results file-share contains all RESTler output plus all bugs found by the job. For Replay step file-share contains logs after reproducing all the bugs found by Fuzz step. 