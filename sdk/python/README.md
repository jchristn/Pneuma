# Pneuma Python SDK

A lightweight Python client for the [Pneuma REST API](../../REST_API.md). It wraps
every documented endpoint with a small, predictable method surface: calls return
parsed JSON (a `dict` or `list`), or `None` for `204 No Content`, and raise
`PneumaError` on any non-2xx response.

## Requirements

- Python >= 3.9
- [`requests`](https://pypi.org/project/requests/) (installed automatically)

## Installation

From this directory (`sdk/python`):

```bash
pip install .
```

Or for development (editable install):

```bash
pip install -e .
```

You can also just add the `pneuma_sdk` package directory to your `PYTHONPATH`; the
only runtime dependency is `requests`.

## Usage

```python
from pneuma_sdk import PneumaClient, PneumaError

# Loopback base URLs are normalized to 127.0.0.1 automatically.
client = PneumaClient("http://127.0.0.1:8080")

# Authenticate. The session token is stored on the client and sent as
# `Authorization: Bearer <token>` for subsequent calls.
client.login("admin@pneuma", "password")

# System
print(client.health())

# Subjects + ingestion
subject = client.create_subject({
    "displayName": "Some Person",
    "type": "Person",
    "description": "An example subject.",
})
subject_id = subject["id"]

# Pick ingestion endpoints (embedding + completion) to process the link.
endpoints = client.list_ingestion_endpoints()
embedding_endpoint_id = endpoints["embedding"][0]["id"]
completion_endpoint_id = endpoints["completion"][0]["id"]

client.submit_link(
    subject_id,
    url="https://example.com/track",
    title="A Track",
    embedding_endpoint_id=embedding_endpoint_id,
    completion_endpoint_id=completion_endpoint_id,
)

# Or submit several URLs at once.
bulk = client.submit_links(
    subject_id,
    urls=["https://example.com/a", "https://example.com/b"],
    embedding_endpoint_id=embedding_endpoint_id,
    completion_endpoint_id=completion_endpoint_id,
)
print(f"created {bulk['created']} links")

# List (GET-all) endpoints return a paginated envelope; records are under
# the "objects" key.
jobs = client.list_jobs(status="Queued")
for job in jobs["objects"]:
    print(job)

# Search & ask
print(client.search("guitar solo", max=10))
print(client.query("What genres does this subject work in?", max_results=5))

client.close()
```

### Pagination

Every list (GET-all) method returns an `EnumerationResult` envelope rather than a
bare list:

```python
{
    "success": True,
    "maxResults": 100,
    "skip": 0,
    "totalRecords": 42,
    "recordsRemaining": 0,
    "endOfResults": True,
    "objects": [ ... ],  # the actual records
}
```

Read records from `result["objects"]` and use the metadata to page. Each list
method accepts optional `max_results`, `skip`, `order` (`"asc"`/`"desc"`), and
`search` keyword arguments (mapped to the server's `maxResults`, `skip`, `order`,
and `search` query params):

```python
page = client.list_subjects(max_results=25, skip=0, order="asc", search="jazz")
subjects = page["objects"]
while not page["endOfResults"]:
    page = client.list_subjects(max_results=25, skip=page["skip"] + page["maxResults"])
    subjects.extend(page["objects"])
```

### Settings (system admin)

```python
current = client.get_settings()
print(current["settings"])   # secrets masked as "********"
print(current["meta"])       # sections, secretFields, secretMask

# Submitting a secret still equal to "********" preserves the stored value.
settings = current["settings"]
settings["somePlainOption"] = "new-value"
result = client.update_settings(settings)
print(result["restartRequired"], result["message"])
```

### Error handling

```python
try:
    client.get_subject("does-not-exist")
except PneumaError as err:
    print(err.status)  # e.g. 404
    print(err.body)    # parsed { error, message, context } payload
```

### Constructing with an existing token

```python
client = PneumaClient("http://127.0.0.1:8080", token="an-existing-session-token")
print(client.validate_token())
```

## Method reference

Grouped by API area (all mirror `REST_API.md`). List (GET-all) methods return a
paginated envelope (read `["objects"]`) and accept optional
`max_results`, `skip`, `order`, `search` keyword arguments (omitted below for
brevity):

- **System**: `health()`
- **Tokens**: `login(email, password, tenant_id=None)`, `validate_token()`,
  `token_details()`, `logout()`
- **Tenants**: `list_tenants()`, `create_tenant(tenant)`, `get_tenant(id)`,
  `update_tenant(id, tenant)`, `delete_tenant(id)`
- **Users**: `list_users(tenant_id=None)`, `create_user(user)`, `get_user(id)`,
  `update_user(id, user)`, `delete_user(id)`
- **Credentials**: `create_credential(credential)`, `list_credentials()`,
  `get_credential(id)`, `delete_credential(id)`
- **RBAC**: `list_roles()`/`create_role()`/`get_role()`/`update_role()`/`delete_role()`,
  the same set for `*_permission`, plus `list_assignments(user_id)`,
  `create_assignment(assignment)`, `delete_assignment(id)`, and
  `list_audit(tenant_id=None)`
- **Subjects**: `list_subjects()`, `create_subject(subject)`, `get_subject(id)`,
  `update_subject(id, subject)`, `delete_subject(id)`
- **Links & ingestion**: `submit_link(subject_id, url, title=None,
  embedding_endpoint_id=None, completion_endpoint_id=None)`,
  `submit_links(subject_id, urls, embedding_endpoint_id=None,
  completion_endpoint_id=None)`, `list_ingestion_endpoints()`,
  `list_subject_links(subject_id)`, `list_links()`, `get_link(id)`,
  `delete_link(id)`
- **Jobs**: `list_jobs(status=None)`, `get_job(id)`, `restart_job(id)`
- **Model runners**: `list_model_runners()`, `create_model_runner(runner)`,
  `get_model_runner(id)`, `update_model_runner(id, runner)`,
  `delete_model_runner(id)`
- **Prompts**: `list_prompts()`, `create_prompt(prompt)`, `get_prompt(id)`,
  `update_prompt(id, prompt)`, `delete_prompt(id)`
- **Settings**: `get_settings()`, `update_settings(settings)`
- **Request history**: `list_request_history(**filters)`,
  `request_history_summary(**filters)`, `get_request_history(id)`,
  `delete_request_history(id)`
- **Knowledge graph**: `get_node(id)`, `get_neighbors(id)`, `get_edges(id)`
- **Search & ask**: `search(q, max=20)`, `query(question, max_results=8)`

## Test harness

`tests/test_harness.py` runs a small end-to-end smoke test (health, login,
listings, create subject, submit link, list jobs). It prints `PASS`/`FAIL` per
step, exits non-zero on failure, and exits `0` (SKIP) if the server is
unreachable.

Run directly:

```bash
python tests/test_harness.py                     # defaults to http://127.0.0.1:8080
python tests/test_harness.py http://127.0.0.1:8080
```

Or under pytest:

```bash
pytest tests/test_harness.py
```

Environment overrides: `PNEUMA_BASE_URL`, `PNEUMA_ADMIN_EMAIL`, `PNEUMA_ADMIN_PASSWORD`.
