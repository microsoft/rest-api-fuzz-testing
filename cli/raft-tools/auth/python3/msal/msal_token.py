import msal
import os
import json
import sys

class RaftJsonDict(dict):
    def __init__(self):
        super(RaftJsonDict, self).__init__()

    def __getitem__(self, key):
        for k in self.keys():
            if k.lower() == key.lower():
                break
        return super(RaftJsonDict, self).__getitem__(key)

    def get(self, key):
        for k in self.keys():
            if k.lower() == key.lower():
                break
        return super(RaftJsonDict, self).get(key)

    @staticmethod
    def raft_json_object_hook(x):
        r = RaftJsonDict()
        for k in x:
            r[k] = x[k]
        return r


def get_token(client_id, tenant_id, secret, scopes, authority_uri, audience):

    if authority_uri:
        authority = f"{authority_uri}/{tenant_id}"
    else:
        authority = f"https://login.microsoftonline.com/{tenant_id}"

    if not scopes:
        if not audience:
            scopes = [f"{client_id}/.default"]
        else:
            scopes = [f"{audience}/.default"]

    app = msal.ConfidentialClientApplication(client_id, authority=authority, client_credential=secret)
    return app.acquire_token_for_client(scopes)

def token_from_env_variable(env_variable_name):
    auth_params = os.environ.get(f"RAFT_{env_variable_name}") or os.environ.get(env_variable_name)
    if auth_params:
        auth = json.loads(auth_params, object_hook=RaftJsonDict.raft_json_object_hook)
        print("Getting MSAL token")
        token = get_token(auth['client'], auth['tenant'], auth['secret'], auth.get('scopes'), auth.get('authorityUri'), auth.get('audience'))
        print("Token created")
        return f'{token["token_type"]} {token["access_token"]}'
    else:
        print(f"Authentication parameters are not set in environment variable {env_variable_name}")
        return None

if __name__ == "__main__":
    token = token_from_env_variable(sys.argv[1])
    print(token)