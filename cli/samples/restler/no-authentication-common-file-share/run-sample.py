# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.
 
import pathlib
import sys
import os

cur_dir = os.path.dirname(os.path.abspath(__file__))
sys.path.append(os.path.join(cur_dir, '..', '..', '..'))
from raft_sdk.raft_service import RaftCLI, RaftJobConfig

def run(compile, fuzz, sample_host):
    # instantiate RAFT CLI
    cli = RaftCLI()
    substitutions = {
        '{sample.host}' : sample_host
    }
    # Create compilation step job configuratin
    compile_job_config = RaftJobConfig(file_path = compile, substitutions=substitutions)
    # add webhook metadata that will be included in every triggered webhook by Compile job
    compile_job_config.add_metadata({"branch":"wizbangFeature"})

    print('Compile')
    # create a new job with the Compile config and get new job ID
    # in compile_job
    compile_job = cli.new_job(compile_job_config)

    # wait for a job with ID from compile_job to finish the run
    cli.poll(compile_job['jobId'])

    substitutions['{compile.jobId}'] = compile_job['jobId']
    # create a new job config with Fuzz configuration JSON
    fuzz_job_config = RaftJobConfig(file_path = fuzz, substitutions=substitutions)
    print('Fuzz')
    # add webhook metadata that will included in every triggered webhook by Fuzz job
    fuzz_job_config.add_metadata({"branch":"wizbangFeature"})
    # create new fuzz job configuration
    fuzz_job = cli.new_job(fuzz_job_config)
    # wait for job ID from fuzz_job to finish the run
    cli.poll(fuzz_job['jobId'])

if __name__ == "__main__":
    if len(sys.argv) != 2:
        print('Please provide host under test as an argument that will be used to\
substitute {sample.host} in compile.json and fuzz.json config files')
    else:
        host = sys.argv[1]
    run(os.path.join(cur_dir, "sample.restler.compile.json"),
        os.path.join(cur_dir, "sample.restler.fuzz.json"),
        host)