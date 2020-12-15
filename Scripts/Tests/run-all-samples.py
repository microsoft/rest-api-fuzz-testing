# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import pathlib
import sys
import os
import json
import urllib.parse
import time

cur_dir = os.path.dirname(os.path.abspath(__file__))
cli_dir = os.path.join(cur_dir, '..', '..', 'cli')
sys.path.append(cli_dir)
from raft_sdk.raft_service import RaftCLI, RaftJobConfig

def find_files():
    configs = {}
    fs = ['zap', 'compile', 'test', 'fuzz']
    for root, _, files in os.walk(cli_dir):
        for f in fs:
            j = f + '.json'
            if j in files:
                if not configs.get(root):
                    configs[root] = {}
                configs[root][f] = os.path.join(root, j)

            y = f + '.yaml'
            if y in files:
                if not configs.get(root):
                    configs[root] = {}
                configs[root][f] = os.path.join(root, y)

    return configs

def wait(configs, count, task_name, job_id_key):
    completed_count = 0
    while completed_count < count:
        for c in configs:
            if configs[c].get(task_name):
                status = cli.job_status(configs[c][job_id_key])
                completed, _ = cli.is_completed(status)
                if completed:
                    completed_count += 1
                cli.print_status(status)
        for _ in range(1,9) :
            sys.stdout.write('.')
            sys.stdout.flush()
            time.sleep(1)
        print('.')


def compile(cli, configs):
    compile_count = 0
    for c in configs:
        if configs[c].get('compile'):
            compile_job_config = RaftJobConfig(file_path=configs[c]['compile'], substitutions=subs)
            compile_job = cli.new_job(compile_job_config)
            configs[c]['compile_job_id'] = compile_job['jobId'] 
            compile_count = compile_count + 1
    print('Compiling all ' + str(compile_count) + ' samples ...')
    wait(configs, compile_count, 'compile', 'compile_job_id')


def test(cli, configs):
    test_count = 0
    for c in configs:
        if configs[c].get('test'):
            subs['{compile.jobId}'] = configs[c]['compile_job_id']
            test_job_config = RaftJobConfig(file_path=configs[c]['test'], substitutions=subs)
            test_job = cli.new_job(test_job_config)
            configs[c]['test_job_id'] = test_job['jobId'] 
            test_count = test_count + 1
    print('Testing all ' + str(test_count) + ' samples ...')
    wait(configs, test_count, 'test', 'test_job_id')


def fuzz_and_zap(cli, configs):
    fuzz_count = 0
    zap_count = 0
    for c in configs:
        if configs[c].get('fuzz'):
            subs['{compile.jobId}'] = configs[c]['compile_job_id']
            fuzz_job_config = RaftJobConfig(file_path=configs[c]['fuzz'], substitutions=subs)
            fuzz_job = cli.new_job(fuzz_job_config)
            configs[c]['fuzz_job_id'] = fuzz_job['jobId'] 
            fuzz_count = fuzz_count + 1

        if configs[c].get('zap'):
            zap_job_config = RaftJobConfig(file_path=configs[c]['zap'], substitutions=subs)
            zap_job = cli.new_job(zap_job_config)
            configs[c]['zap_job_id'] = zap_job['jobId'] 
            zap_count = zap_count + 1

    print('Fuzzing all ' + str(fuzz_count) + ' and ZAP: ' + str(zap_count) + ' samples ...')
    wait(configs, fuzz_count, 'fuzz', 'fuzz_job_id')
    wait(configs, zap_count, 'zap', 'zap_job_id')

if __name__ == "__main__":
    if len(sys.argv) == 1:
        print('Please provide a host on command line that does not require authentication. Something like: test-host.com')

    sample_host = sys.argv[1]

    cli = RaftCLI()
    subs = {
       '{sample.host}' : sample_host,
       '{defaults.deploymentName}' : cli.definitions.deployment,
       '{ci-run}': 'all-samples'
    }
    configs = find_files()
    compile(cli, configs)
    test(cli, configs)
    fuzz_and_zap(cli, configs)
