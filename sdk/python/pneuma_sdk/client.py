"""Pneuma Python SDK client.

A thin, dependency-light wrapper around the Pneuma REST API. Every method mirrors
an endpoint documented in REST_API.md. Successful calls return parsed JSON (a
dict or list), or ``None`` for ``204 No Content`` responses. Any non-2xx
response raises :class:`PneumaError`.
"""

from typing import Any, Dict, List, Optional

import requests


class PneumaError(Exception):
    """Raised when the Pneuma API returns a non-2xx response.

    Attributes:
        status: The HTTP status code returned by the server.
        body: The parsed JSON error body if available, otherwise the raw text.
    """

    def __init__(self, status: int, body: Any):
        self.status = status
        self.body = body
        message = f"Pneuma API request failed with status {status}"
        if isinstance(body, dict):
            code = body.get("error")
            detail = body.get("message")
            parts = [p for p in (code, detail) if p]
            if parts:
                message = f"{message}: {' - '.join(str(p) for p in parts)}"
        elif body:
            message = f"{message}: {body}"
        super().__init__(message)


def _normalize_base_url(base_url: str) -> str:
    """Trim trailing slashes and force loopback hosts to 127.0.0.1.

    ``localhost`` can resolve to an IPv6 address (``::1``) on some platforms,
    which the server may not bind to. Using the explicit IPv4 loopback avoids
    that class of connection failure.
    """
    if not base_url:
        base_url = "http://127.0.0.1:8080"
    base_url = base_url.rstrip("/")
    base_url = base_url.replace("://localhost:", "://127.0.0.1:")
    base_url = base_url.replace("://localhost/", "://127.0.0.1/")
    if base_url.endswith("://localhost"):
        base_url = base_url[: -len("localhost")] + "127.0.0.1"
    return base_url


class PneumaClient:
    """Client for the Pneuma REST API.

    Example:
        client = PneumaClient("http://127.0.0.1:8080")
        client.login("admin@pneuma", "password")
        print(client.health())
    """

    def __init__(self, base_url: str, token: Optional[str] = None):
        """Create a client.

        Args:
            base_url: Base URL of the Pneuma server. Loopback hosts are forced to
                127.0.0.1 (see module docs). Defaults to the local server if
                falsy.
            token: Optional existing session/bearer token.
        """
        self.base_url = _normalize_base_url(base_url)
        self.token = token
        self._session = requests.Session()
        self._session.headers.update({"Accept": "application/json"})

    # ------------------------------------------------------------------
    # Internals
    # ------------------------------------------------------------------

    def _headers(self, extra: Optional[Dict[str, str]] = None) -> Dict[str, str]:
        headers: Dict[str, str] = {}
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        if extra:
            headers.update(extra)
        return headers

    def _request(
        self,
        method: str,
        path: str,
        params: Optional[Dict[str, Any]] = None,
        json_body: Optional[Any] = None,
        headers: Optional[Dict[str, str]] = None,
    ) -> Any:
        """Perform an HTTP request and return parsed JSON, or None on 204.

        Raises:
            PneumaError: If the response status is not in the 2xx range.
        """
        url = f"{self.base_url}{path}"
        # Drop query params whose value is None so callers can pass optionals freely.
        clean_params = None
        if params:
            clean_params = {k: v for k, v in params.items() if v is not None}

        request_headers = self._headers(headers)
        if json_body is not None:
            request_headers.setdefault("Content-Type", "application/json")

        response = self._session.request(
            method,
            url,
            params=clean_params,
            json=json_body,
            headers=request_headers,
        )

        if not (200 <= response.status_code < 300):
            raise PneumaError(response.status_code, _safe_body(response))

        if response.status_code == 204 or not response.content:
            return None

        try:
            return response.json()
        except ValueError:
            return response.text

    @staticmethod
    def _list_params(
        max_results: Optional[int] = None,
        skip: Optional[int] = None,
        order: Optional[str] = None,
        search: Optional[str] = None,
        extra: Optional[Dict[str, Any]] = None,
    ) -> Dict[str, Any]:
        """Build the camelCase query-param dict shared by paginated list endpoints.

        Maps the snake_case Python arguments (``max_results``, ``skip``,
        ``order``, ``search``) to the server's camelCase query keys. ``None``
        values are left in place; :meth:`_request` drops them before sending.

        Args:
            max_results: Optional page size (server ``maxResults``, default 100).
            skip: Optional number of records to skip (server ``skip``).
            order: Optional sort order, ``"asc"`` or ``"desc"`` (default ``desc``).
            search: Optional substring filter.
            extra: Optional endpoint-specific params (already camelCase) merged in.

        Returns:
            A params dict suitable for passing to :meth:`_request`.
        """
        params: Dict[str, Any] = {
            "maxResults": max_results,
            "skip": skip,
            "order": order,
            "search": search,
        }
        if extra:
            params.update(extra)
        return params

    # ------------------------------------------------------------------
    # System
    # ------------------------------------------------------------------

    def health(self) -> Any:
        """GET /v1.0/api/health -> health payload."""
        return self._request("GET", "/v1.0/api/health")

    # ------------------------------------------------------------------
    # Tokens / authentication
    # ------------------------------------------------------------------

    def login(
        self, email: str, password: str, tenant_id: Optional[str] = None
    ) -> Any:
        """POST /v1.0/token. Stores the returned token for subsequent calls.

        Args:
            email: User email.
            password: User password.
            tenant_id: Optional tenant to scope the session to.

        Returns:
            The full token response payload.
        """
        body: Dict[str, Any] = {"email": email, "password": password}
        if tenant_id is not None:
            body["tenantId"] = tenant_id
        result = self._request("POST", "/v1.0/token", json_body=body)
        if isinstance(result, dict) and result.get("token"):
            self.token = result["token"]
        return result

    def validate_token(self) -> Any:
        """GET /v1.0/token -> principal summary for the current token."""
        return self._request("GET", "/v1.0/token")

    def token_details(self) -> Any:
        """GET /v1.0/token/details -> decoded authentication context."""
        return self._request("GET", "/v1.0/token/details")

    def logout(self) -> None:
        """DELETE /v1.0/token. Revokes the current session and clears the token."""
        result = self._request("DELETE", "/v1.0/token")
        self.token = None
        return result

    # ------------------------------------------------------------------
    # Tenants (admin)
    # ------------------------------------------------------------------

    def list_tenants(
        self,
        max_results: Optional[int] = None,
        skip: Optional[int] = None,
        order: Optional[str] = None,
        search: Optional[str] = None,
    ) -> Any:
        """GET /v1.0/tenants -> paginated EnumerationResult envelope.

        Args:
            max_results: Optional page size (server ``maxResults``, default 100).
            skip: Optional number of records to skip.
            order: Optional sort order, ``"asc"`` or ``"desc"`` (default ``desc``).
            search: Optional substring filter.

        Returns:
            The EnumerationResult envelope; records are under ``["objects"]``.
        """
        return self._request(
            "GET",
            "/v1.0/tenants",
            params=self._list_params(max_results, skip, order, search),
        )

    def create_tenant(self, tenant: Dict[str, Any]) -> Any:
        """POST /v1.0/tenants."""
        return self._request("POST", "/v1.0/tenants", json_body=tenant)

    def get_tenant(self, tenant_id: str) -> Any:
        """GET /v1.0/tenants/{id}."""
        return self._request("GET", f"/v1.0/tenants/{tenant_id}")

    def update_tenant(self, tenant_id: str, tenant: Dict[str, Any]) -> Any:
        """PUT /v1.0/tenants/{id}."""
        return self._request("PUT", f"/v1.0/tenants/{tenant_id}", json_body=tenant)

    def delete_tenant(self, tenant_id: str) -> None:
        """DELETE /v1.0/tenants/{id}."""
        return self._request("DELETE", f"/v1.0/tenants/{tenant_id}")

    # ------------------------------------------------------------------
    # Users
    # ------------------------------------------------------------------

    def list_users(
        self,
        tenant_id: Optional[str] = None,
        max_results: Optional[int] = None,
        skip: Optional[int] = None,
        order: Optional[str] = None,
        search: Optional[str] = None,
    ) -> Any:
        """GET /v1.0/users (admin may pass tenant_id) -> EnumerationResult envelope.

        Args:
            tenant_id: Optional tenant to scope the listing to (admin only).
            max_results: Optional page size (server ``maxResults``, default 100).
            skip: Optional number of records to skip.
            order: Optional sort order, ``"asc"`` or ``"desc"`` (default ``desc``).
            search: Optional substring filter.

        Returns:
            The EnumerationResult envelope; records are under ``["objects"]``.
        """
        return self._request(
            "GET",
            "/v1.0/users",
            params=self._list_params(
                max_results, skip, order, search, extra={"tenantId": tenant_id}
            ),
        )

    def create_user(self, user: Dict[str, Any]) -> Any:
        """POST /v1.0/users with a CreateUserRequest body."""
        return self._request("POST", "/v1.0/users", json_body=user)

    def get_user(self, user_id: str) -> Any:
        """GET /v1.0/users/{id}."""
        return self._request("GET", f"/v1.0/users/{user_id}")

    def update_user(self, user_id: str, user: Dict[str, Any]) -> Any:
        """PUT /v1.0/users/{id}."""
        return self._request("PUT", f"/v1.0/users/{user_id}", json_body=user)

    def delete_user(self, user_id: str) -> None:
        """DELETE /v1.0/users/{id}."""
        return self._request("DELETE", f"/v1.0/users/{user_id}")

    # ------------------------------------------------------------------
    # Credentials
    # ------------------------------------------------------------------

    def create_credential(self, credential: Dict[str, Any]) -> Any:
        """POST /v1.0/credentials. The raw secretKey is returned only once."""
        return self._request("POST", "/v1.0/credentials", json_body=credential)

    def list_credentials(
        self,
        max_results: Optional[int] = None,
        skip: Optional[int] = None,
        order: Optional[str] = None,
        search: Optional[str] = None,
    ) -> Any:
        """GET /v1.0/credentials -> paginated EnumerationResult envelope.

        Args:
            max_results: Optional page size (server ``maxResults``, default 100).
            skip: Optional number of records to skip.
            order: Optional sort order, ``"asc"`` or ``"desc"`` (default ``desc``).
            search: Optional substring filter.

        Returns:
            The EnumerationResult envelope; records are under ``["objects"]``.
        """
        return self._request(
            "GET",
            "/v1.0/credentials",
            params=self._list_params(max_results, skip, order, search),
        )

    def get_credential(self, credential_id: str) -> Any:
        """GET /v1.0/credentials/{id}."""
        return self._request("GET", f"/v1.0/credentials/{credential_id}")

    def delete_credential(self, credential_id: str) -> None:
        """DELETE /v1.0/credentials/{id}."""
        return self._request("DELETE", f"/v1.0/credentials/{credential_id}")

    # ------------------------------------------------------------------
    # Roles, Permissions, Assignments, Audit (RBAC, admin)
    # ------------------------------------------------------------------

    def list_roles(
        self,
        max_results: Optional[int] = None,
        skip: Optional[int] = None,
        order: Optional[str] = None,
        search: Optional[str] = None,
    ) -> Any:
        """GET /v1.0/roles -> paginated EnumerationResult envelope.

        Args:
            max_results: Optional page size (server ``maxResults``, default 100).
            skip: Optional number of records to skip.
            order: Optional sort order, ``"asc"`` or ``"desc"`` (default ``desc``).
            search: Optional substring filter.

        Returns:
            The EnumerationResult envelope; records are under ``["objects"]``.
        """
        return self._request(
            "GET",
            "/v1.0/roles",
            params=self._list_params(max_results, skip, order, search),
        )

    def create_role(self, role: Dict[str, Any]) -> Any:
        """POST /v1.0/roles."""
        return self._request("POST", "/v1.0/roles", json_body=role)

    def get_role(self, role_id: str) -> Any:
        """GET /v1.0/roles/{id}."""
        return self._request("GET", f"/v1.0/roles/{role_id}")

    def update_role(self, role_id: str, role: Dict[str, Any]) -> Any:
        """PUT /v1.0/roles/{id}."""
        return self._request("PUT", f"/v1.0/roles/{role_id}", json_body=role)

    def delete_role(self, role_id: str) -> None:
        """DELETE /v1.0/roles/{id}."""
        return self._request("DELETE", f"/v1.0/roles/{role_id}")

    def list_permissions(
        self,
        max_results: Optional[int] = None,
        skip: Optional[int] = None,
        order: Optional[str] = None,
        search: Optional[str] = None,
    ) -> Any:
        """GET /v1.0/permissions -> paginated EnumerationResult envelope.

        Args:
            max_results: Optional page size (server ``maxResults``, default 100).
            skip: Optional number of records to skip.
            order: Optional sort order, ``"asc"`` or ``"desc"`` (default ``desc``).
            search: Optional substring filter.

        Returns:
            The EnumerationResult envelope; records are under ``["objects"]``.
        """
        return self._request(
            "GET",
            "/v1.0/permissions",
            params=self._list_params(max_results, skip, order, search),
        )

    def create_permission(self, permission: Dict[str, Any]) -> Any:
        """POST /v1.0/permissions."""
        return self._request("POST", "/v1.0/permissions", json_body=permission)

    def get_permission(self, permission_id: str) -> Any:
        """GET /v1.0/permissions/{id}."""
        return self._request("GET", f"/v1.0/permissions/{permission_id}")

    def update_permission(self, permission_id: str, permission: Dict[str, Any]) -> Any:
        """PUT /v1.0/permissions/{id}."""
        return self._request(
            "PUT", f"/v1.0/permissions/{permission_id}", json_body=permission
        )

    def delete_permission(self, permission_id: str) -> None:
        """DELETE /v1.0/permissions/{id}."""
        return self._request("DELETE", f"/v1.0/permissions/{permission_id}")

    def list_assignments(
        self,
        user_id: str,
        max_results: Optional[int] = None,
        skip: Optional[int] = None,
        order: Optional[str] = None,
        search: Optional[str] = None,
    ) -> Any:
        """GET /v1.0/assignments?userId=<id> -> EnumerationResult envelope.

        Args:
            user_id: Required user whose assignments to list.
            max_results: Optional page size (server ``maxResults``, default 100).
            skip: Optional number of records to skip.
            order: Optional sort order, ``"asc"`` or ``"desc"`` (default ``desc``).
            search: Optional substring filter.

        Returns:
            The EnumerationResult envelope; records are under ``["objects"]``.
        """
        return self._request(
            "GET",
            "/v1.0/assignments",
            params=self._list_params(
                max_results, skip, order, search, extra={"userId": user_id}
            ),
        )

    def create_assignment(self, assignment: Dict[str, Any]) -> Any:
        """POST /v1.0/assignments."""
        return self._request("POST", "/v1.0/assignments", json_body=assignment)

    def delete_assignment(self, assignment_id: str) -> None:
        """DELETE /v1.0/assignments/{id}."""
        return self._request("DELETE", f"/v1.0/assignments/{assignment_id}")

    def list_audit(
        self,
        tenant_id: Optional[str] = None,
        max_results: Optional[int] = None,
        skip: Optional[int] = None,
        order: Optional[str] = None,
        search: Optional[str] = None,
    ) -> Any:
        """GET /v1.0/audit (admin may pass tenant_id) -> EnumerationResult envelope.

        Args:
            tenant_id: Optional tenant to scope the listing to (admin only).
            max_results: Optional page size (server ``maxResults``, default 100).
            skip: Optional number of records to skip.
            order: Optional sort order, ``"asc"`` or ``"desc"`` (default ``desc``).
            search: Optional substring filter.

        Returns:
            The EnumerationResult envelope; records are under ``["objects"]``.
        """
        return self._request(
            "GET",
            "/v1.0/audit",
            params=self._list_params(
                max_results, skip, order, search, extra={"tenantId": tenant_id}
            ),
        )

    # ------------------------------------------------------------------
    # Subjects
    # ------------------------------------------------------------------

    def list_subjects(
        self,
        max_results: Optional[int] = None,
        skip: Optional[int] = None,
        order: Optional[str] = None,
        search: Optional[str] = None,
    ) -> Any:
        """GET /v1.0/subjects -> paginated EnumerationResult envelope.

        Args:
            max_results: Optional page size (server ``maxResults``, default 100).
            skip: Optional number of records to skip.
            order: Optional sort order, ``"asc"`` or ``"desc"`` (default ``desc``).
            search: Optional substring filter.

        Returns:
            The EnumerationResult envelope; records are under ``["objects"]``.
        """
        return self._request(
            "GET",
            "/v1.0/subjects",
            params=self._list_params(max_results, skip, order, search),
        )

    def create_subject(self, subject: Dict[str, Any]) -> Any:
        """POST /v1.0/subjects."""
        return self._request("POST", "/v1.0/subjects", json_body=subject)

    def get_subject(self, subject_id: str) -> Any:
        """GET /v1.0/subjects/{id}."""
        return self._request("GET", f"/v1.0/subjects/{subject_id}")

    def update_subject(self, subject_id: str, subject: Dict[str, Any]) -> Any:
        """PUT /v1.0/subjects/{id}."""
        return self._request(
            "PUT", f"/v1.0/subjects/{subject_id}", json_body=subject
        )

    def delete_subject(self, subject_id: str) -> None:
        """DELETE /v1.0/subjects/{id}."""
        return self._request("DELETE", f"/v1.0/subjects/{subject_id}")

    # ------------------------------------------------------------------
    # Content links & ingestion
    # ------------------------------------------------------------------

    def submit_link(
        self,
        subject_id: str,
        url: str,
        title: Optional[str] = None,
        embedding_endpoint_id: Optional[str] = None,
        completion_endpoint_id: Optional[str] = None,
    ) -> Any:
        """POST /v1.0/subjects/{subjectId}/links. Enqueues an ingestion job.

        Args:
            subject_id: The subject to attach the link to.
            url: The content URL to ingest.
            title: Optional operator-facing title.
            embedding_endpoint_id: Partio embedding endpoint id (sent as
                ``embeddingEndpointId``).
            completion_endpoint_id: Partio completion endpoint id (sent as
                ``completionEndpointId``).
        """
        body: Dict[str, Any] = {"url": url}
        if title is not None:
            body["title"] = title
        if embedding_endpoint_id is not None:
            body["embeddingEndpointId"] = embedding_endpoint_id
        if completion_endpoint_id is not None:
            body["completionEndpointId"] = completion_endpoint_id
        return self._request(
            "POST", f"/v1.0/subjects/{subject_id}/links", json_body=body
        )

    def submit_links(
        self,
        subject_id: str,
        urls: List[str],
        embedding_endpoint_id: Optional[str] = None,
        completion_endpoint_id: Optional[str] = None,
    ) -> Any:
        """POST /v1.0/subjects/{subjectId}/links/bulk. Enqueues one job per URL.

        Args:
            subject_id: The subject to attach the links to.
            urls: The content URLs to ingest.
            embedding_endpoint_id: Partio embedding endpoint id (sent as
                ``embeddingEndpointId``).
            completion_endpoint_id: Partio completion endpoint id (sent as
                ``completionEndpointId``).

        Returns:
            ``{ "created": <int>, "links": [...] }``.
        """
        body: Dict[str, Any] = {"urls": urls}
        if embedding_endpoint_id is not None:
            body["embeddingEndpointId"] = embedding_endpoint_id
        if completion_endpoint_id is not None:
            body["completionEndpointId"] = completion_endpoint_id
        return self._request(
            "POST", f"/v1.0/subjects/{subject_id}/links/bulk", json_body=body
        )

    def list_ingestion_endpoints(self) -> Any:
        """GET /v1.0/ingestion/endpoints.

        Returns:
            ``{ "embedding": [...], "completion": [...] }`` where each entry has
            ``id``, ``name``, ``model``, ``apiFormat`` and ``active``.
        """
        return self._request("GET", "/v1.0/ingestion/endpoints")

    def list_subject_links(
        self,
        subject_id: str,
        max_results: Optional[int] = None,
        skip: Optional[int] = None,
        order: Optional[str] = None,
        search: Optional[str] = None,
    ) -> Any:
        """GET /v1.0/subjects/{subjectId}/links -> EnumerationResult envelope.

        Args:
            subject_id: The subject whose links to list.
            max_results: Optional page size (server ``maxResults``, default 100).
            skip: Optional number of records to skip.
            order: Optional sort order, ``"asc"`` or ``"desc"`` (default ``desc``).
            search: Optional substring filter.

        Returns:
            The EnumerationResult envelope; records are under ``["objects"]``.
        """
        return self._request(
            "GET",
            f"/v1.0/subjects/{subject_id}/links",
            params=self._list_params(max_results, skip, order, search),
        )

    def list_links(
        self,
        max_results: Optional[int] = None,
        skip: Optional[int] = None,
        order: Optional[str] = None,
        search: Optional[str] = None,
    ) -> Any:
        """GET /v1.0/links -> paginated EnumerationResult envelope.

        Args:
            max_results: Optional page size (server ``maxResults``, default 100).
            skip: Optional number of records to skip.
            order: Optional sort order, ``"asc"`` or ``"desc"`` (default ``desc``).
            search: Optional substring filter.

        Returns:
            The EnumerationResult envelope; records are under ``["objects"]``.
        """
        return self._request(
            "GET",
            "/v1.0/links",
            params=self._list_params(max_results, skip, order, search),
        )

    def get_link(self, link_id: str) -> Any:
        """GET /v1.0/links/{id}."""
        return self._request("GET", f"/v1.0/links/{link_id}")

    def get_link_ingestion_log(self, link_id: str) -> Any:
        """GET /v1.0/links/{id}/log — per-step ingestion log for a link."""
        return self._request("GET", f"/v1.0/links/{link_id}/log")

    def get_link_atoms(self, link_id: str) -> Any:
        """GET /v1.0/links/{id}/atoms — DocumentAtom semantic cells artifact.

        Raises an API error with status 404 if that stage hasn't run yet.
        """
        return self._request("GET", f"/v1.0/links/{link_id}/atoms")

    def get_link_chunks(self, link_id: str) -> Any:
        """GET /v1.0/links/{id}/chunks — Partio chunks artifact.

        Raises an API error with status 404 if that stage hasn't run yet.
        """
        return self._request("GET", f"/v1.0/links/{link_id}/chunks")

    def get_link_vectors(self, link_id: str) -> Any:
        """GET /v1.0/links/{id}/vectors — Partio embeddings artifact.

        Raises an API error with status 404 if that stage hasn't run yet.
        """
        return self._request("GET", f"/v1.0/links/{link_id}/vectors")

    def get_link_subgraph(self, link_id: str) -> Any:
        """GET /v1.0/links/{id}/subgraph — candidate subgraph artifact.

        Raises an API error with status 404 if that stage hasn't run yet.
        """
        return self._request("GET", f"/v1.0/links/{link_id}/subgraph")

    def delete_link(self, link_id: str) -> None:
        """DELETE /v1.0/links/{id}."""
        return self._request("DELETE", f"/v1.0/links/{link_id}")

    # ------------------------------------------------------------------
    # Jobs
    # ------------------------------------------------------------------

    def list_jobs(
        self,
        status: Optional[str] = None,
        max_results: Optional[int] = None,
        skip: Optional[int] = None,
        order: Optional[str] = None,
        search: Optional[str] = None,
    ) -> Any:
        """GET /v1.0/jobs (optional status filter) -> EnumerationResult envelope.

        Args:
            status: Optional job status filter.
            max_results: Optional page size (server ``maxResults``, default 100).
            skip: Optional number of records to skip.
            order: Optional sort order, ``"asc"`` or ``"desc"`` (default ``desc``).
            search: Optional substring filter.

        Returns:
            The EnumerationResult envelope; records are under ``["objects"]``.
        """
        return self._request(
            "GET",
            "/v1.0/jobs",
            params=self._list_params(
                max_results, skip, order, search, extra={"status": status}
            ),
        )

    def get_job(self, job_id: str) -> Any:
        """GET /v1.0/jobs/{id} -> { job, events }."""
        return self._request("GET", f"/v1.0/jobs/{job_id}")

    def restart_job(self, job_id: str) -> Any:
        """POST /v1.0/jobs/{id}/restart. Requeues a failed job."""
        return self._request("POST", f"/v1.0/jobs/{job_id}/restart")

    def stop_job(self, job_id: str) -> Any:
        """POST /v1.0/jobs/{id}/stop. Stops (cancels) a queued or in-flight job."""
        return self._request("POST", f"/v1.0/jobs/{job_id}/stop")

    def get_job_log(self, job_id: str) -> Any:
        """GET /v1.0/jobs/{id}/log -> { job, events }. Poll for a follow-logs view."""
        return self._request("GET", f"/v1.0/jobs/{job_id}/log")

    # ------------------------------------------------------------------
    # Model runners (admin)
    # ------------------------------------------------------------------

    def list_model_runners(
        self,
        max_results: Optional[int] = None,
        skip: Optional[int] = None,
        order: Optional[str] = None,
        search: Optional[str] = None,
    ) -> Any:
        """GET /v1.0/model-runners -> paginated EnumerationResult envelope.

        Args:
            max_results: Optional page size (server ``maxResults``, default 100).
            skip: Optional number of records to skip.
            order: Optional sort order, ``"asc"`` or ``"desc"`` (default ``desc``).
            search: Optional substring filter.

        Returns:
            The EnumerationResult envelope; records are under ``["objects"]``.
        """
        return self._request(
            "GET",
            "/v1.0/model-runners",
            params=self._list_params(max_results, skip, order, search),
        )

    def create_model_runner(self, runner: Dict[str, Any]) -> Any:
        """POST /v1.0/model-runners."""
        return self._request("POST", "/v1.0/model-runners", json_body=runner)

    def get_model_runner(self, runner_id: str) -> Any:
        """GET /v1.0/model-runners/{id}."""
        return self._request("GET", f"/v1.0/model-runners/{runner_id}")

    def update_model_runner(self, runner_id: str, runner: Dict[str, Any]) -> Any:
        """PUT /v1.0/model-runners/{id}."""
        return self._request(
            "PUT", f"/v1.0/model-runners/{runner_id}", json_body=runner
        )

    def delete_model_runner(self, runner_id: str) -> None:
        """DELETE /v1.0/model-runners/{id}."""
        return self._request("DELETE", f"/v1.0/model-runners/{runner_id}")

    # ------------------------------------------------------------------
    # Prompts (admin)
    # ------------------------------------------------------------------

    def list_prompts(
        self,
        max_results: Optional[int] = None,
        skip: Optional[int] = None,
        order: Optional[str] = None,
        search: Optional[str] = None,
    ) -> Any:
        """GET /v1.0/prompts -> paginated EnumerationResult envelope.

        Args:
            max_results: Optional page size (server ``maxResults``, default 100).
            skip: Optional number of records to skip.
            order: Optional sort order, ``"asc"`` or ``"desc"`` (default ``desc``).
            search: Optional substring filter.

        Returns:
            The EnumerationResult envelope; records are under ``["objects"]``.
        """
        return self._request(
            "GET",
            "/v1.0/prompts",
            params=self._list_params(max_results, skip, order, search),
        )

    def create_prompt(self, prompt: Dict[str, Any]) -> Any:
        """POST /v1.0/prompts."""
        return self._request("POST", "/v1.0/prompts", json_body=prompt)

    def get_prompt(self, prompt_id: str) -> Any:
        """GET /v1.0/prompts/{id}."""
        return self._request("GET", f"/v1.0/prompts/{prompt_id}")

    def update_prompt(self, prompt_id: str, prompt: Dict[str, Any]) -> Any:
        """PUT /v1.0/prompts/{id}."""
        return self._request("PUT", f"/v1.0/prompts/{prompt_id}", json_body=prompt)

    def delete_prompt(self, prompt_id: str) -> None:
        """DELETE /v1.0/prompts/{id}."""
        return self._request("DELETE", f"/v1.0/prompts/{prompt_id}")

    # ------------------------------------------------------------------
    # Settings (system admin)
    # ------------------------------------------------------------------

    def get_settings(self) -> Any:
        """GET /v1.0/settings -> { success, settings, meta }.

        Secret fields in ``settings`` are masked as ``"********"``. ``meta``
        describes the available sections and which fields are secret::

            { "sections": [{ "key", "label", "requiresRestart" }],
              "secretFields": [...], "secretMask": "********" }

        Returns:
            The settings payload with masked secrets.
        """
        return self._request("GET", "/v1.0/settings")

    def update_settings(self, settings: Dict[str, Any]) -> Any:
        """PUT /v1.0/settings with the settings dict as the JSON body.

        Submitting a secret field still equal to ``"********"`` preserves the
        value stored on the server.

        Args:
            settings: The settings dict to persist.

        Returns:
            ``{ success, restartRequired, message, meta }``.
        """
        return self._request("PUT", "/v1.0/settings", json_body=settings)

    # ------------------------------------------------------------------
    # Request history
    # ------------------------------------------------------------------

    def list_request_history(self, **filters: Any) -> Any:
        """GET /v1.0/api/request-history.

        Accepts filter keyword arguments: method, statusCode, pathContains,
        fromUtc, toUtc, pageNumber, pageSize (and admin tenantId, userId).
        """
        return self._request("GET", "/v1.0/api/request-history", params=filters or None)

    def request_history_summary(self, **filters: Any) -> Any:
        """GET /v1.0/api/request-history/summary.

        Accepts filter keyword arguments: fromUtc, toUtc, bucketMinutes.
        """
        return self._request(
            "GET", "/v1.0/api/request-history/summary", params=filters or None
        )

    def get_request_history(self, entry_id: str) -> Any:
        """GET /v1.0/api/request-history/{id} (full entry with headers/bodies)."""
        return self._request("GET", f"/v1.0/api/request-history/{entry_id}")

    def delete_request_history(self, entry_id: str) -> None:
        """DELETE /v1.0/api/request-history/{id}."""
        return self._request("DELETE", f"/v1.0/api/request-history/{entry_id}")

    # ------------------------------------------------------------------
    # Knowledge graph (user)
    # ------------------------------------------------------------------

    def get_node(self, node_id: str) -> Any:
        """GET /v1.0/graph/nodes/{id}."""
        return self._request("GET", f"/v1.0/graph/nodes/{node_id}")

    def get_neighbors(self, node_id: str) -> Any:
        """GET /v1.0/graph/nodes/{id}/neighbors."""
        return self._request("GET", f"/v1.0/graph/nodes/{node_id}/neighbors")

    def get_edges(self, node_id: str) -> Any:
        """GET /v1.0/graph/nodes/{id}/edges."""
        return self._request("GET", f"/v1.0/graph/nodes/{node_id}/edges")

    # ------------------------------------------------------------------
    # Search & ask (user)
    # ------------------------------------------------------------------

    def search(self, q: str, max: int = 20) -> Any:
        """GET /v1.0/search?q=<query>&max=<n>."""
        return self._request("GET", "/v1.0/search", params={"q": q, "max": max})

    def query(self, question: str, max_results: int = 8) -> Any:
        """POST /v1.0/query -> grounded answer with sources."""
        return self._request(
            "POST",
            "/v1.0/query",
            json_body={"question": question, "maxResults": max_results},
        )

    # ------------------------------------------------------------------
    # Lifecycle
    # ------------------------------------------------------------------

    def close(self) -> None:
        """Close the underlying HTTP session."""
        self._session.close()

    def __enter__(self) -> "PneumaClient":
        return self

    def __exit__(self, exc_type, exc_val, exc_tb) -> bool:
        self.close()
        return False


def _safe_body(response: "requests.Response") -> Any:
    """Return the parsed JSON error body, falling back to raw text."""
    try:
        return response.json()
    except ValueError:
        return response.text
