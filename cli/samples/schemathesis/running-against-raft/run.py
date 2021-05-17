# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import pathlib
import sys
import os
import json
import urllib.parse

cur_dir = os.path.dirname(os.path.abspath(__file__))
sys.path.append(os.path.join(cur_dir, '..', '..', '..'))
from raft_sdk.raft_service import RaftJobConfig, RaftCLI

def run(cli, run_file):
    run_config = RaftJobConfig(file_path=run_file,
                        substitutions={'{defaults.deploymentName}' : (RaftCLI().definitions.deployment)})
    job = cli.new_job(run_config)
    print(job)
    cli.poll(job['jobId'])

if __name__ == "__main__":
    if '--local' in sys.argv:
        from raft_local import RaftLocalCLI
        cli = RaftLocalCLI()
    else:
        cli = RaftCLI()
    
    run(cli, os.path.join(cur_dir, "schemathesis.json"))