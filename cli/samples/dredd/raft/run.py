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

def run(run_config):
    cli = RaftCLI()
    substitutions = {
        '{defaults.deploymentName}': cli.definitions.deployment
    }
    run_job_config = RaftJobConfig(file_path=run_config, substitutions=substitutions)
    run_job = cli.new_job(run_job_config)
    # wait for a job with ID from compile_job to finish the run
    cli.poll(run_job['jobId'])


if __name__ == "__main__":
    run(os.path.join(cur_dir, "dredd.json"))