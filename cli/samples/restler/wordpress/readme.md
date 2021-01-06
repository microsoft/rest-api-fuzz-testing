
#### restler/wordpress

This sample runs [RESTler](https://github.com/microsoft/restler-fuzzer) on a [Wordpress](https://hub.docker.com/_/wordpress) docker container deployed as part of the RAFT job.

You can run the sample by executing `python run.py compile test fuzz`. This sample creates three jobs: **Compile**, **Test** and **Fuzz**. (You can also run **Compile** only step by running `python run.py compile`)

The script pipes output of **Compile** job into **Test** and then into **Fuzz** jobs by mounting a output file-share from **Compile** step as an input read-only file-share to **Fuzz** step. All results from the RAFT job run will be stored in file-share named **wordpress** in your RAFT Azure storage.

Each step will print Results URL to the console - this is a URL that you can paste in a web browser to access Azure file share produced by the job run. For Compile step the results file-share  will contain RESTler compile step output. For Test and Fuzz steps the results file-share contains all RESTler output plus all bugs found by the job. For Replay step file-share contains logs after reproducing all the bugs found by Fuzz step. 

Currently this sample is not fully functionality since RESTler when executing Test of Fuzz steps: [https://github.com/microsoft/restler-fuzzer/issues/130](https://github.com/microsoft/restler-fuzzer/issues/130)