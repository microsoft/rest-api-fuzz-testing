#### restler/no-authentication-common-file-share

This sample runs [RESTler](https://github.com/microsoft/restler-fuzzer) on [https://petstore.swagger.io](https://petstore.swagger.io) endpoint.

This sample is much the same as the no-authentication sample, the main difference being the use
of the  **rootFileshare** field. Because this is defined, look for the folder with the job id under this
file share.

The **rootFileshare** is a great way to organize related jobs together. 

You can run the sample in your Azure RAFT deployment by executing `python run.py`. 
You can run the sample in your local docker service by executing `python run.py --local`.

This sample creates three jobs: Compile, Fuzz, Replay. The script pipes output of Compile job into Fuzz by mounting a output file-share from Compile step as an input read-only file-share to Fuzz step. Then Fuzz step output file-share is mounted are read-only file-share to Replay step.

Each step will print Results URL to the console - this is a URL that you can paste in a web browser to access Azure file share produced by the job run. For Compile step the results file-share  will contain RESTler compile step output. For Fuzz step the results file-share contains all RESTler output plus all bugs found by the job. For Replay step file-share contains logs after reproducing all the bugs found by Fuzz step.

All results from the run (compile and fuzz) will be in your deployment's Azure storage account file share named `sample`. Compile run is going to have a `compile-` prefix in the folder name and fuzz run is going to have `fuzz-` prefix in the folder name.


