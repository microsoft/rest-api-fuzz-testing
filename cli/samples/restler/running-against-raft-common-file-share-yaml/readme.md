#### restler/running-against-raft-common-file-share-yaml

This sample runs [RESTler](https://github.com/microsoft/restler-fuzzer) on your RAFT deployment. All configurations are defined as yaml files.

You can run the sample in your Azure RAFT deployment by executing `python run.py`. 
You can run the sample in your local docker service by executing `python run.py --local`. You still have to have Azure RAFT service deployed, since this is service under test.
This sample creates three jobs: Compile, Test and Fuzz. 
The script pipes output of Compile job into Test and then into Fuzz jobs by mounting a output file-share from Compile step as an input read-only file-share to Fuzz step. 

Each step will print Results URL to the console - this is a URL that you can paste in a web browser to access Azure file share produced by the job run. For Compile step the results file-share  will contain RESTler compile step output. For Fuzz step the results file-share contains all RESTler output plus all bugs found by the job. For Replay step file-share contains logs after reproducing all the bugs found by Fuzz step. 

All results from Compile, Test and Fuzz steps will be stored in a file share named `raft` in RAFT's Azure storage account.