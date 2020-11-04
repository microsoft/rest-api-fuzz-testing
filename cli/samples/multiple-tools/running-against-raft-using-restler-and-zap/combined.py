# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import pathlib
import sys
import os
import json
import urllib.parse

cur_dir = os.path.dirname(os.path.abspath(__file__))
sys.path.append(os.path.join(cur_dir, '..', '..', '..'))
import raft

def run(compile, fuzz, sample_host):
    cli = raft.RaftCLI()
    subs = {
        '{sample.host}' : sample_host,
        '{defaults.deploymentName}' : cli.definitions.deployment

    }
    compile_job_config = raft.RaftJobConfig(file_path=compile, substitutions=subs)
    print('Compile')
    compile_job = cli.new_job(compile_job_config)
    cli.poll(compile_job['jobId'])
    subs['{compile.jobId}'] = compile_job['jobId']

    fuzz_job_config = raft.RaftJobConfig(file_path=fuzz, substitutions=subs)
    print('Fuzz')
    fuzz_job = cli.new_job(fuzz_job_config)
    cli.poll(fuzz_job['jobId'])

if __name__ == "__main__":
    if len(sys.argv) != 2:
        print('Please provide host under test as an argument that will be used to \
substitute {sample.host} in compile.json and fuzz.json config files')
    else:
        host = sys.argv[1]
    run(os.path.join(cur_dir, "compile.json"),
        os.path.join(cur_dir, "fuzz.json"),
        host)