# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.
 
import pathlib
import sys
import os
import json

cur_dir = os.path.dirname(os.path.abspath(__file__))
cli_dir = os.path.join(cur_dir, '..', '..', '..', '..')
sys.path.append(cli_dir)
from raft_sdk.raft_service import RaftCLI, RaftJobConfig, RaftJobError, RaftDefinitions, RaftJsonDict


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
        if "--local" in sys.argv:
            from raft_local import RaftLocalCLI
            cli = RaftLocalCLI(network='host')

        else:
            with open(os.path.join(cli_dir, 'defaults.json'), 'r') as defaults_json:
                defaults = json.load(defaults_json, object_hook=RaftJsonDict.raft_json_object_hook)
                if len(sys.argv) > 3 and sys.argv[3] == '--secret':
                    defaults['secret'] = sys.argv[4]

            # instantiate RAFT CLI
            cli = RaftCLI(defaults)

        compile_job_id = None
        subs = {
            "{ci-run}" : f"{build_id}",
            "{build-url}" : os.environ['SYSTEM_COLLECTIONURI'] if os.environ.get('SYSTEM_COLLECTIONURI') else "",
            "{build-id}" : os.environ['BUILD_BUILDID'] if os.environ.get('BUILD_BUILDID') else ""
        }
        if 'compile' in sys.argv:
            compile_job_id = run(cli, os.path.join(cur_dir, 'compile.json'), subs)
            subs['{compile.jobId}'] = compile_job_id

        if 'test' in sys.argv:
            run(cli, os.path.join(cur_dir, "test.json"), subs), 

        if 'test-fuzz-lean' in sys.argv:
            run(cli, os.path.join(cur_dir, "test-fuzz-lean.json"), subs), 

    except RaftJobError as ex:
        print(f'ERROR: {ex.message}')
        sys.exit(1)