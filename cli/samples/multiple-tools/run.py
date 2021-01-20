# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import pathlib
import sys
import os
import json
import urllib.parse

cur_dir = os.path.dirname(os.path.abspath(__file__))
sys.path.append(os.path.join(cur_dir, '..', '..'))
import raft

def compile(cli, compile, subs):
    compile_job_config = raft.RaftJobConfig(file_path=compile, substitutions=subs)
    print('Compile')
    return cli.new_job(compile_job_config)

def fuzz(cli, fuzz, subs):
    fuzz_job_config = raft.RaftJobConfig(file_path=fuzz, substitutions=subs)
    print('Fuzz')
    return cli.new_job(fuzz_job_config)

def run(compile_config, fuzz_config):
    cli = raft.RaftCLI()
    subs = {
        '{defaults.deploymentName}' : cli.definitions.deployment
    }
    compile_job = compile(cli, compile_config, subs)
    cli.poll(compile_job['jobId'])
    subs['{compile.jobId}'] = compile_job['jobId']
    fuzz_job = fuzz(cli, fuzz_config, subs)
    cli.poll(fuzz_job['jobId'])

if __name__ == "__main__":
    run(os.path.join(cur_dir, "compile.yaml"), os.path.join(cur_dir, "fuzz.json"))