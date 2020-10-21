# Job Commands

## job-create \<jobDefinition.json\>

Creates a new job. The \<jobDefinition.json\> file defines the job and tasks to run.

* **--duration** specifies how long the job should run. This is a TimeSpan value.</br>
  The duration can also be specified in the job definition. If it is defined in the job definition and this parameter is used, this value will be the one used. It overrides the job definition value.
* **--read-only-mounts** specifies which file shares will be mounted to the new job as a read-only file share.</br>
  This is helpful when multiple jobs need to access the same information.</br>

```
Usage: --read-only-mounts '[{"FileShareName":"grade-track-compile-48297e1a-9cb4-4578-8fa1-15bd8949affb", "MountPath": "/job-compile"}]'
```

* **--read-write-mounts** specified file shares which will be mounted with read-write access.

```
Usage: --read-write-mounts '[{"FileShareName":"MyData", "MountPath": "/personalData "}]'
```

* **--poll \<int\>** takes a value which is used as the job status polling interval in seconds.</br>
  Polling terminates once the job has completed.
* **--metadata** Arbitrary key/value pairs can be passed to a job.</br>
  This data will be returned in webhooks. In this way you can track things like branch names, change authors, bug numbers, etc in your jobs.
If you have a logic app which handles your bugFound webhook by creating a bug in your bug tracking system, you could have this data available in the bug.

```
Usage: --metadata  '{"BuildNumber":"pipelineBuildNumber", "Author": "John"}'
```

Returns a \<jobId\> as json. 

```
Example: {'jobId': '0a0fd91f-8592-4c9d-97dd-01c9c3c44159'}
```

The jobId that is returned is a string that will contain a guid, if you decide to use a namePrefix in the job definition, the guid will be prepended with the prefix.

## job-status \<jobId\></br>

Gets the status of a job.

```
Usage: job-status ffc4a296-f85d-4122-b49b-8074b88c9755
```

## job-list --look-back-hours</br>

List the status of all jobs. By default the command will return the status of all jobs over the last 24 hours.
Use `--look-back-hours` to specify a different time frame. For example to look back over the last hour

```
Usage: job-list --look-back-hours 1
```

## job-update \<jobId\> \<jobdefinition.json\></br>

Deploy a job definition to an existing job. This is useful when the job is deployed with the "isIdling" flag set to true
which tells the service to not delete the container when the job has completed. In this way it is possible to quickly
deploy a new job without waiting for container creation.

It is also possible to use `ssh` to log into the container if manual exploration of the container is needed.
If the container is not running for some reason, the job will be created as normal.
If the job container creation failed for some reason, the job will not be created. You can check the application insights log for failures.

## job-delete \<jobId\></br>

Deletes the job. By default jobs are garbage collected when they have completed their run.
However, if "isIdling" is set to true, manual job deletion is required.

## job-results-url \<jobId\></br>

Returns a url to the storage file share where the job results are stored. Use the url in your browser to go directly to the results.
