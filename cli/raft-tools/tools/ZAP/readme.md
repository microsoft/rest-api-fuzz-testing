## ZAP configuration

- **config.json**
    
Required by RAFT in order to deploy ZAP tool

- **requirements.txt**, **run.py**, **scan.py**

Implementation of a ZAP driver that integrates with RAFT. The implementation uses utilities from `/cli/raft-tools/libs/python3` for authentication implementation, logging and posting job status updates to RAFT.