#### restler/replay-a-bug

This sample runs [RESTler](https://github.com/microsoft/restler-fuzzer) on existing RESTler fuzzing results.

In this sample, the job description JSON will cause RAFT to replay a bug found
in a previous run of the tool, either to reproduce the bug and ensure it's valid, or
to verify that the bug has been fixed.

During development you will want to be sure that the developed fix actually fixes the reported issue.
Replaying the original bug is a way to test the fix without needing to run the fuzz task all over again.

In **replay-common-file-share.json** there are two tasks defined. The first task shows you how to replay a specific
bug by defining the `{bugBucket}` value. 

The second task, will replay all the bugs found from when fuzzing. 

The only different between the two example files is the use of the common file share defined in **rootFileshare**.

`replay-common-file-share.json` - this config file used to re-run bugs found by RESTler with job configs that used `fileShare` field to store results in common file share.

`replay.json` - this config file used to re-run bugs found by RESTler and each job run is stored in a dedicated Azure file share.