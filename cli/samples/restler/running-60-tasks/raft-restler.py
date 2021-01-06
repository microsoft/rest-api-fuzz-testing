# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.

import pathlib
import sys
import os
import json
import urllib.parse
import copy
import random

cur_dir = os.path.dirname(os.path.abspath(__file__))
sys.path.append(os.path.join(cur_dir, '..', '..', '..'))
from raft_sdk.raft_service import RaftCLI, RaftJobConfig

def run(compile, test, host):
    cli = RaftCLI()
    substitutions = {
        '{host}': host
    }
    compile_job_config = RaftJobConfig(file_path=compile, substitutions=substitutions)

    compile_task = compile_job_config.config['tasks'][0]
    #use first task as template and create 30 compile task
    compile_tasks = []
    for t in range(30):
        new_task = copy.deepcopy(compile_task)
        new_task['outputFolder'] = compile_task['outputFolder'] + f"-{t}"
        new_task['toolConfiguration']['compileConfiguration']['mutationsSeed'] = random.randint(0, 1000)
        compile_tasks.append(new_task)

    compile_job_config.config['tasks'] = compile_tasks

    print('Compile')
    # create a new job with the Compile config and get new job ID
    # in compile_job
    compile_job = cli.new_job(compile_job_config)
    # wait for a job with ID from compile_job to finish the run
    cli.poll(compile_job['jobId'])

    substitutions['{compile.jobId}'] = compile_job['jobId']

    print('Test')
    test_job_config = RaftJobConfig(file_path=test, substitutions=substitutions)

    task_test_fuzz_lean = test_job_config.config['tasks'][0]
    task_test = test_job_config.config['tasks'][1]
    test_tasks = []
    for t in range(30):
        new_task_test = copy.deepcopy(task_test)
        new_task_test_fuzz_lean = copy.deepcopy(task_test_fuzz_lean)

        new_task_test['outputFolder'] = task_test['outputFolder'] + f"-{t}"
        new_task_test['toolConfiguration']['runConfiguration']['inputFolderPath'] += '/' + compile_tasks[t]['outputFolder']

        new_task_test_fuzz_lean['outputFolder'] = task_test_fuzz_lean['outputFolder'] + f"-{t}"
        new_task_test_fuzz_lean['toolConfiguration']['runConfiguration']['inputFolderPath'] += '/' + compile_tasks[t]['outputFolder']

        test_tasks.append(new_task_test)
        test_tasks.append(new_task_test_fuzz_lean)

    test_job_config.config['tasks'] = test_tasks
    test_job = cli.new_job(test_job_config)
    cli.poll(test_job['jobId'])

if __name__ == "__main__":
    if len(sys.argv) != 2:
        print('Please provide host under test as an argument that will be used to \
substitute {sample.host} in compile.json and fuzz.json config files')
    else:
        host = sys.argv[1]
    run(os.path.join(cur_dir, "compile-60.json"),
        os.path.join(cur_dir, "test-60.json"),
        host)