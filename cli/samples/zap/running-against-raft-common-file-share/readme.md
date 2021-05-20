#### zap/running-against-raft

In this sample, the job description JSON will cause RAFT to execute ZAP against the
RAFT service itself, defining a common file share called **raft**. This can be helpful
when you want to collect results from multiple job runs under a single file share. 

You can run the sample in you RAFT Azure deployment by calling `python run.py`.

You can run the sample in your local docker service by calling `python run.py --local`. But you have to have RAFT Azure service deployed, since it is used as a service under test by the sample.