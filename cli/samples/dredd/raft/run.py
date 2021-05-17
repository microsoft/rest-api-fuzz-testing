# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import pathlib
import sys
import os
import json
import urllib.parse

cur_dir = os.path.dirname(os.path.abspath(__file__))
sys.path.append(os.path.join(cur_dir, '..', '..', '..'))
from raft_sdk.raft_service import RaftCLI, RaftJobConfig

def run(cli, run_config):
    substitutions = {
        '{defaults.deploymentName}': (RaftCLI()).definitions.deployment
    }
    run_job_config = RaftJobConfig(file_path=run_config, substitutions=substitutions)
    run_job = cli.new_job(run_job_config)
    # wait for a job with ID from compile_job to finish the run
    cli.poll(run_job['jobId'])


if __name__ == "__main__":
    if '--local' in sys.argv:
        from raft_local import RaftLocalCLI
        cli = RaftLocalCLI(network='host')
    else:
        cli = RaftCLI()
    run(cli, os.path.join(cur_dir, "dredd.json"))