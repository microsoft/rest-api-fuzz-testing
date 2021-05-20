#### restler/self-contained/swagger-petstore

This sample runs [RESTler](https://github.com/microsoft/restler-fuzzer) on a `petstore` docker container deployed alongside RESTler container.

You can run the sample in your Azure RAFT deployment by executing `python run.py`. 
You can run the sample in your local docker service by executing `python run.py --local`.

This sample creates four jobs: Compile, Test, Fuzz and Reaplay. 
The script pipes output of Compile job into Test and then into Fuzz jobs by mounting a output file-share from Compile step as an input read-only file-share to Fuzz step. Output of Fuzz run is used for Replay run. Replay run replays all bugs found by Fuzz step.

Each step will print Results URL to the console - this is a URL that you can paste in a web browser to access Azure file share produced by the job run. For Compile step the results file-share  will contain RESTler compile step output. For Fuzz step the results file-share contains all RESTler output plus all bugs found by the job. For Replay step file-share contains logs after reproducing all the bugs found by Fuzz step. 