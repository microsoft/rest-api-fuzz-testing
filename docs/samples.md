# RAFT Sample Configurations

We have included a number of sample job definitions, and python scripts
in with the CLI package.

We hope that you take advantage of them to evaluate RAFT's operation,
or as templates for automating RESTler, ZAP and Dredd, or any additional tools
you might want to [onboard](how-to-onboard-a-tool.md).

The samples are located in the **cli/samples** folder under the root of
the RAFT repo, and are organized into three folders:  **Zap**, **RESTler**, **Dredd**,
and **multiple-tools**, the latter of which shows how to execute more than
one tool in a single job. Each sample folder includes it's own `readme.md` file explaining what the sample is about and how to run it.

**IMPORTANT NOTE:**</br>
The sample python scripts have been written to take advantage of your Azure deployment of RAFT.
In most sample directories there is a python script that runs the sample and is an example of how to use
our python SDK to run jobs. In many of the sample job definition files there are substitutions that are 
made via the python script. 

Because of this, the files typically will not run without modification with the `raft_local.py` script.
See [here](how-to-use-raft-local.md) for more information about running RAFT on your desktop with `raft_local.py`.