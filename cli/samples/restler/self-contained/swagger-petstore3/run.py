# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.
 
import pathlib
import sys
import os
import json

cur_dir = os.path.dirname(os.path.abspath(__file__))
cli_dir = os.path.join(cur_dir, '..', '..', '..', '..')
sys.path.append(cli_dir)
from raft_sdk.raft_service import RaftCLI, RaftJobConfig, RaftJobError, RaftDefinitions


def run(cli, config, subs):
    # Create compilation job configuration
    job_config = RaftJobConfig(file_path=config, substitutions=subs)
    print(f'Running {config}')
    # submit a new job with the Compile config and get new job ID
    job = cli.new_job(job_config)
    # wait for a job with ID from compile_job to finish the run
    cli.poll(job['jobId'])
    return job['jobId']

if __name__ == "__main__":
    try:
        defaults = None

        if sys.argv[1] == '--build':
            build_id = sys.argv[2].replace(".", "-")
        print(f"BUILD ID : {build_id}")

        with open(os.path.join(cli_dir, 'defaults.json'), 'r') as defaults_json:
            defaults = json.load(defaults_json)
            if sys.argv[3] == '--secret':
                defaults['secret'] = sys.argv[4]

        # instantiate RAFT CLI
        cli = RaftCLI(defaults)
        defs = RaftDefinitions(defaults)

        compile_job_id = None
        subs = {
            "{ci-run}" : f"{build_id}",
            "{build-url}" : os.environ['SYSTEM_COLLECTIONURI'],
            "{build-id}" : os.environ['BUILD_BUILDID'],
            "{raft-subscription}": defs.subscription,
            "{raft-resource-group}" : defs.resource_group,
            "{raft-storage-account}" : defs.storage_account
        }
        for arg in sys.argv[1:]:
            if arg == 'compile':
                compile_job_id = run(cli, os.path.join(cur_dir, 'compile.json'), subs)
                subs['{compile.jobId}'] = compile_job_id

            if arg == 'test':
                run(cli, os.path.join(cur_dir, "test.json"), subs), 

            if arg == 'test-fuzz-lean':
                run(cli, os.path.join(cur_dir, "test-fuzz-lean.json"), subs), 

    except RaftJobError as ex:
        print(f'ERROR: {ex.message}')
        sys.exit(1)