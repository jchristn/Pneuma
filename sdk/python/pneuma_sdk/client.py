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
        labels: Optional[List[str]] = None,
        tags: Optional[Dict[str, str]] = None,
        embedding_endpoint_id: Optional[str] = None,
        completion_endpoint_id: Optional[str] = None,
    ) -> Any:
        """POST /v1.0/subjects/{subjectId}/links. Enqueues an ingestion job.

        Args:
            subject_id: The subject to attach the link to.
            url: The content URL to ingest.
            title: Optional operator-facing title.
            labels: Optional labels (strings) attached to every chunk and to the
                link's source graph node, so retrieval can be scoped to them.
            tags: Optional key/value tags attached to every chunk and to the
                link's source graph node, so retrieval can be scoped to them.
            embedding_endpoint_id: Embedding endpoint id (sent as
                ``embeddingEndpointId``).
            completion_endpoint_id: Completion endpoint id (sent as
                ``completionEndpointId``).
        """
        body: Dict[str, Any] = {"url": url}
        if title is not None:
            body["title"] = title
        if labels is not None:
            body["labels"] = labels
        if tags is not None:
            body["tags"] = tags
        if embedding_endpoint_id is not None:
            body["embeddingEndpointId"] = embedding_endpoint_id
        if completion_endpoint_id is not None:
            body["completionEndpointId"] = completion_endpoint_id
        return self._request(
            "POST", f"/v1.0/subjects/{subject_id}/links", json_body=body
        )

    def submit_content(
        self,
        subject_id: str,
        content: str,
        content_type: str = "text/markdown",
        title: Optional[str] = None,
        external_key: Optional[str] = None,
        labels: Optional[List[str]] = None,
        tags: Optional[Dict[str, str]] = None,
    ) -> Any:
        """POST /v1.0/subjects/{subjectId}/content. Stores content and queues its ingestion.

        Args:
            subject_id: The subject to add the content to.
            content: The content text.
            content_type: ``text/plain``, ``text/markdown``, ``text/html``, or ``application/json``.
            title: Optional title (also the document title in chunk headers).
            external_key: Optional stable key; pushing again with it replaces the content.
            labels: Optional labels attached to every chunk.
            tags: Optional key/value tags attached to every chunk.

        Returns:
            ``{ index, statusCode, replaced, link, jobId }``.
        """
        body: Dict[str, Any] = {"content": content, "contentType": content_type}
        if title is not None:
            body["title"] = title
        if external_key is not None:
            body["externalKey"] = external_key
        if labels is not None:
            body["labels"] = labels
        if tags is not None:
            body["tags"] = tags
        return self._request("POST", f"/v1.0/subjects/{subject_id}/content", json_body=body)

    def submit_content_batch(self, subject_id: str, items: List[Dict[str, Any]]) -> Any:
        """POST /v1.0/subjects/{subjectId}/content/batch: up to 100 items, each reported.

        Args:
            subject_id: The subject to add the content to.
            items: Items with the same keys as the ``submit_content`` body (``content``, ``contentType``, ...).

        Returns:
            ``{ accepted, rejected, results }``.
        """
        return self._request("POST", f"/v1.0/subjects/{subject_id}/content/batch", json_body={"items": items})

    def submit_links(
        self,
        subject_id: str,
        urls: List[str],
        labels: Optional[List[str]] = None,
        tags: Optional[Dict[str, str]] = None,
        embedding_endpoint_id: Optional[str] = None,
        completion_endpoint_id: Optional[str] = None,
    ) -> Any:
        """POST /v1.0/subjects/{subjectId}/links/bulk. Enqueues one job per URL.

        Args:
            subject_id: The subject to attach the links to.
            urls: The content URLs to ingest.
            labels: Optional labels applied to every URL in the batch.
            tags: Optional key/value tags applied to every URL in the batch.
            embedding_endpoint_id: Embedding endpoint id (sent as
                ``embeddingEndpointId``).
            completion_endpoint_id: Completion endpoint id (sent as
                ``completionEndpointId``).

        Returns:
            ``{ "created": <int>, "links": [...] }``.
        """
        body: Dict[str, Any] = {"urls": urls}
        if labels is not None:
            body["labels"] = labels
        if tags is not None:
            body["tags"] = tags
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
            ``id``, ``type`` ("Embedding" | "Completion"), ``name``, ``model``,
            ``endpoint``, ``apiFormat``, ``provider`` (one of ``OpenAI``,
            ``OpenAICompatible``, ``Gemini``, ``Ollama``, ``AzureOpenAI``,
            ``Anthropic``, ``Bedrock``, ``VoyageAI``, ``VertexAI``),
            ``deployment``, ``apiVersion``, ``region``, ``project``,
            ``accessKeyId`` and ``active``. Secret material (``apiKey``,
            ``secretAccessKey``, ``sessionToken``) is write-only and never
            returned.
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

    # ---- Crawl plans and crawl operations ----

    def list_crawl_plan_types(self) -> Any:
        """GET /v1.0/crawl-plan-types: the supported crawler types with their settings schemas."""
        return self._request("GET", "/v1.0/crawl-plan-types")

    def create_crawl_plan(self, subject_id: str, plan: Dict[str, Any]) -> Any:
        """POST /v1.0/subjects/{subjectId}/crawl-plans: create a plan that keeps a subject in sync with a source.

        Secret settings are write-only: stored encrypted and never returned (``secretsSet`` names them).
        """
        return self._request("POST", f"/v1.0/subjects/{subject_id}/crawl-plans", json_body=plan)

    def list_crawl_plans(self, subject_id: Optional[str] = None, max_results: Optional[int] = None, skip: Optional[int] = None) -> Any:
        """GET /v1.0/crawl-plans, optionally for one subject."""
        return self._request("GET", "/v1.0/crawl-plans", params=self._clean({"subjectId": subject_id, "maxResults": max_results, "skip": skip}))

    def get_crawl_plan(self, plan_id: str) -> Any:
        """GET /v1.0/crawl-plans/{id}."""
        return self._request("GET", f"/v1.0/crawl-plans/{plan_id}")

    def update_crawl_plan(self, plan_id: str, plan: Dict[str, Any]) -> Any:
        """PUT /v1.0/crawl-plans/{id}: replace the configuration; omitted secrets keep their stored values."""
        return self._request("PUT", f"/v1.0/crawl-plans/{plan_id}", json_body=plan)

    def delete_crawl_plan(self, plan_id: str, delete_links: bool = False) -> Any:
        """DELETE /v1.0/crawl-plans/{id}; ``delete_links`` also deletes the links the plan created."""
        return self._request("DELETE", f"/v1.0/crawl-plans/{plan_id}", params={"deleteLinks": "true"} if delete_links else None)

    def test_crawl_plan_draft(self, plan: Dict[str, Any], from_plan_id: Optional[str] = None) -> Any:
        """POST /v1.0/crawl-plans/test: test a draft plan's connection without saving it."""
        return self._request("POST", "/v1.0/crawl-plans/test", params={"fromPlanId": from_plan_id} if from_plan_id else None, json_body=plan)

    def test_crawl_plan(self, plan_id: str) -> Any:
        """POST /v1.0/crawl-plans/{id}/test: each connectivity step."""
        return self._request("POST", f"/v1.0/crawl-plans/{plan_id}/test")

    def preview_crawl_plan(self, plan_id: str) -> Any:
        """POST /v1.0/crawl-plans/{id}/preview: what a run would do; nothing is changed."""
        return self._request("POST", f"/v1.0/crawl-plans/{plan_id}/preview")

    def start_crawl_plan(self, plan_id: str) -> Any:
        """POST /v1.0/crawl-plans/{id}/start: start an operation now (409 when already running)."""
        return self._request("POST", f"/v1.0/crawl-plans/{plan_id}/start")

    def stop_crawl_plan(self, plan_id: str) -> Any:
        """POST /v1.0/crawl-plans/{id}/stop."""
        return self._request("POST", f"/v1.0/crawl-plans/{plan_id}/stop")

    def list_crawl_plan_operations(self, plan_id: str, max_results: Optional[int] = None, skip: Optional[int] = None) -> Any:
        """GET /v1.0/crawl-plans/{id}/operations, newest first."""
        return self._request("GET", f"/v1.0/crawl-plans/{plan_id}/operations", params=self._clean({"maxResults": max_results, "skip": skip}))

    def list_crawl_plan_objects(self, plan_id: str, status: Optional[str] = None, max_results: Optional[int] = None, skip: Optional[int] = None) -> Any:
        """GET /v1.0/crawl-plans/{id}/objects, optionally by status (Active, Missing, Failed, Excluded)."""
        return self._request("GET", f"/v1.0/crawl-plans/{plan_id}/objects", params=self._clean({"status": status, "maxResults": max_results, "skip": skip}))

    def list_crawl_operations(self, plan_id: Optional[str] = None, status: Optional[str] = None, max_results: Optional[int] = None, skip: Optional[int] = None) -> Any:
        """GET /v1.0/crawl-operations, optionally by plan and status."""
        return self._request("GET", "/v1.0/crawl-operations", params=self._clean({"planId": plan_id, "status": status, "maxResults": max_results, "skip": skip}))

    def get_crawl_operation(self, operation_id: str) -> Any:
        """GET /v1.0/crawl-operations/{id}."""
        return self._request("GET", f"/v1.0/crawl-operations/{operation_id}")

    def list_crawl_operation_objects(self, operation_id: str, action: Optional[str] = None, max_results: Optional[int] = None, skip: Optional[int] = None) -> Any:
        """GET /v1.0/crawl-operations/{id}/objects, optionally by action."""
        return self._request("GET", f"/v1.0/crawl-operations/{operation_id}/objects", params=self._clean({"action": action, "maxResults": max_results, "skip": skip}))

    def confirm_crawl_deletions(self, operation_id: str) -> Any:
        """POST /v1.0/crawl-operations/{id}/confirm-deletions: run a held operation's deletions."""
        return self._request("POST", f"/v1.0/crawl-operations/{operation_id}/confirm-deletions")

    @staticmethod
    def _clean(params: Dict[str, Any]) -> Optional[Dict[str, Any]]:
        cleaned = {k: v for k, v in params.items() if v is not None}
        return cleaned or None

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

    def set_link_refresh(
        self,
        link_id: str,
        refresh_interval_minutes: Optional[int] = None,
        use_subject_default: bool = False,
    ) -> Any:
        """PUT /v1.0/links/{id} - set a link's scheduled refresh.

        refresh_interval_minutes is 0 (off) or 60 to 525600; use_subject_default follows the subject's default.
        """
        body: Dict[str, Any] = {"useSubjectDefault": use_subject_default}
        if refresh_interval_minutes is not None:
            body["refreshIntervalMinutes"] = refresh_interval_minutes
        return self._request("PUT", f"/v1.0/links/{link_id}", json_body=body)

    def bulk_set_link_refresh(
        self,
        ids: List[str],
        refresh_interval_minutes: Optional[int] = None,
        use_subject_default: bool = False,
    ) -> Any:
        """POST /v1.0/links/refresh-interval - set the scheduled refresh of several links.

        Returns {"updated": n, "skipped": [ids]}.
        """
        body: Dict[str, Any] = {"ids": ids, "useSubjectDefault": use_subject_default}
        if refresh_interval_minutes is not None:
            body["refreshIntervalMinutes"] = refresh_interval_minutes
        return self._request("POST", "/v1.0/links/refresh-interval", json_body=body)

    def refresh_link_now(self, link_id: str) -> Any:
        """POST /v1.0/links/{id}/refresh - check a link for changes now; a changed link is re-ingested."""
        return self._request("POST", f"/v1.0/links/{link_id}/refresh")

    # ------------------------------------------------------------ New subject wizard

    def get_wizard_options(self) -> Any:
        """GET /v1.0/subject-wizard/options - ontology modes the caller may use and the wizard's limits."""
        return self._request("GET", "/v1.0/subject-wizard/options")

    def draft_wizard_step(
        self,
        step: str,
        draft: Dict[str, Any],
        model_runner_id: Optional[str] = None,
        guidance: Optional[str] = None,
        mode: Optional[str] = None,
        count: int = 0,
    ) -> Any:
        """POST /v1.0/subject-wizard/{step} - draft brief, questions, ontology, prompts, or sources.

        Nothing is stored. Returns {"value", "model", "modelRunnerId", "elapsedMs", "warnings", "groundingExcerpt"}.
        """
        body: Dict[str, Any] = {"draft": draft, "count": count}
        if model_runner_id:
            body["modelRunnerId"] = model_runner_id
        if guidance:
            body["guidance"] = guidance
        if mode:
            body["mode"] = mode
        return self._request("POST", f"/v1.0/subject-wizard/{step}", json_body=body)

    def render_wizard_ontology(self, draft: Dict[str, Any]) -> Any:
        """POST /v1.0/subject-wizard/render-ontology - the draft ontology as the classifier will see it."""
        return self._request("POST", "/v1.0/subject-wizard/render-ontology", json_body={"draft": draft})

    def commit_subject_wizard(
        self,
        draft: Dict[str, Any],
        inference_model: Optional[str] = None,
        embedding_model: Optional[str] = None,
        collection: Optional[str] = None,
        ontology_mode: str = "Approve",
    ) -> Any:
        """POST /v1.0/subject-wizard/commit - create the subject, its questions, and its ontology from a draft."""
        body: Dict[str, Any] = {"draft": draft, "ontologyMode": ontology_mode}
        if inference_model:
            body["inferenceModel"] = inference_model
        if embedding_model:
            body["embeddingModel"] = embedding_model
        if collection:
            body["collection"] = collection
        return self._request("POST", "/v1.0/subject-wizard/commit", json_body=body)

    def get_subject_questions(self, subject_id: str) -> Any:
        """GET /v1.0/subjects/{id}/questions - a subject's starter questions."""
        return self._request("GET", f"/v1.0/subjects/{subject_id}/questions")

    def set_subject_questions(self, subject_id: str, questions: List[Dict[str, Any]]) -> Any:
        """PUT /v1.0/subjects/{id}/questions - replace a subject's starter questions ({question, kind})."""
        return self._request("PUT", f"/v1.0/subjects/{subject_id}/questions", json_body={"questions": questions})

    def bulk_create_eval_facts(self, facts: List[Dict[str, Any]]) -> Any:
        """POST /v1.0/eval/facts/bulk - create up to 100 evaluation facts ({subjectId, question, expectedAnswer, category})."""
        return self._request("POST", "/v1.0/eval/facts/bulk", json_body={"facts": facts})

    def get_link_ingestion_log(self, link_id: str) -> Any:
        """GET /v1.0/links/{id}/log — per-step ingestion log for a link."""
        return self._request("GET", f"/v1.0/links/{link_id}/log")

    def get_link_atoms(self, link_id: str) -> Any:
        """GET /v1.0/links/{id}/atoms — DocumentAtom semantic cells artifact.

        Raises an API error with status 404 if that stage hasn't run yet.
        """
        return self._request("GET", f"/v1.0/links/{link_id}/atoms")

    def get_link_chunks(self, link_id: str) -> Any:
        """GET /v1.0/links/{id}/chunks — chunks artifact.

        Raises an API error with status 404 if that stage hasn't run yet.
        """
        return self._request("GET", f"/v1.0/links/{link_id}/chunks")

    def get_link_vectors(self, link_id: str) -> Any:
        """GET /v1.0/links/{id}/vectors — embeddings artifact.

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
    # Ontologies
    # ------------------------------------------------------------------

    def list_ontology_templates(self) -> Any:
        """GET /v1.0/ontology-templates: the built-in templates a tenant can start from."""
        return self._request("GET", "/v1.0/ontology-templates")

    def list_ontologies(self, max_results: Optional[int] = None, skip: Optional[int] = None) -> Any:
        """GET /v1.0/ontologies: the tenant's ontologies (paginated)."""
        return self._request("GET", "/v1.0/ontologies", params=self._clean({"maxResults": max_results, "skip": skip}))

    def create_ontology(self, name: str, description: Optional[str] = None, template: Optional[str] = None, copy_from_version_id: Optional[str] = None) -> Any:
        """POST /v1.0/ontologies: create an ontology whose first version is a draft (empty, from a template, or a copy)."""
        body = {"name": name, "description": description, "template": template, "copyFromVersionId": copy_from_version_id}
        return self._request("POST", "/v1.0/ontologies", json_body={k: v for k, v in body.items() if v is not None})

    def get_ontology(self, ontology_id: str) -> Any:
        """GET /v1.0/ontologies/{id}: the ontology with its versions and the subjects that pin them."""
        return self._request("GET", f"/v1.0/ontologies/{ontology_id}")

    def update_ontology(self, ontology_id: str, name: Optional[str] = None, description: Optional[str] = None) -> Any:
        """PUT /v1.0/ontologies/{id}: rename or re-describe."""
        return self._request("PUT", f"/v1.0/ontologies/{ontology_id}", json_body={"name": name, "description": description})

    def delete_ontology(self, ontology_id: str) -> None:
        """DELETE /v1.0/ontologies/{id} (refused with 409 while a subject pins one of its versions)."""
        return self._request("DELETE", f"/v1.0/ontologies/{ontology_id}")

    def list_ontology_versions(self, ontology_id: str, max_results: Optional[int] = None, skip: Optional[int] = None) -> Any:
        """GET /v1.0/ontologies/{id}/versions: newest first."""
        return self._request("GET", f"/v1.0/ontologies/{ontology_id}/versions", params=self._clean({"maxResults": max_results, "skip": skip}))

    def create_ontology_draft(self, ontology_id: str, based_on_version_id: Optional[str] = None) -> Any:
        """POST /v1.0/ontologies/{id}/versions: a new draft copying a version (by default the newest)."""
        return self._request("POST", f"/v1.0/ontologies/{ontology_id}/versions", json_body={"basedOnVersionId": based_on_version_id})

    def propose_ontology(self, ontology_id: str, request: Dict[str, Any]) -> Any:
        """POST /v1.0/ontologies/{id}/propose: have the inference model propose a new draft.

        ``request`` keys: subjectId, sampleText, modelRunnerId, sampleCells, instructions, language, basedOnVersionId.
        """
        return self._request("POST", f"/v1.0/ontologies/{ontology_id}/propose", json_body=request)

    def get_ontology_version(self, version_id: str) -> Any:
        """GET /v1.0/ontology-versions/{id}: types, rules, concepts, and approval problems."""
        return self._request("GET", f"/v1.0/ontology-versions/{version_id}")

    def update_ontology_version(self, version_id: str, version: Dict[str, Any]) -> Any:
        """PUT /v1.0/ontology-versions/{id}: replace a draft's contents."""
        return self._request("PUT", f"/v1.0/ontology-versions/{version_id}", json_body=version)

    def delete_ontology_version(self, version_id: str) -> None:
        """DELETE /v1.0/ontology-versions/{id}: delete a draft."""
        return self._request("DELETE", f"/v1.0/ontology-versions/{version_id}")

    def approve_ontology_version(self, version_id: str, change_summary: Optional[str] = None) -> Any:
        """POST /v1.0/ontology-versions/{id}/approve (needs Ontology Execute)."""
        return self._request("POST", f"/v1.0/ontology-versions/{version_id}/approve", json_body={"changeSummary": change_summary})

    def retire_ontology_version(self, version_id: str) -> Any:
        """POST /v1.0/ontology-versions/{id}/retire (refused while a subject pins it)."""
        return self._request("POST", f"/v1.0/ontology-versions/{version_id}/retire")

    def diff_ontology_version(self, version_id: str, against: Optional[str] = None) -> Any:
        """GET /v1.0/ontology-versions/{id}/diff: what changed from ``against`` (default the version it was copied from)."""
        return self._request("GET", f"/v1.0/ontology-versions/{version_id}/diff", params=self._clean({"against": against}))

    def get_ontology_definition(self, version_id: str) -> Any:
        """GET /v1.0/ontology-versions/{id}/definition: the text the classifier sees."""
        return self._request("GET", f"/v1.0/ontology-versions/{version_id}/definition")

    def export_ontology_version(self, version_id: str, fmt: str = "turtle", base_iri: Optional[str] = None) -> Any:
        """GET /v1.0/ontology-versions/{id}/export: OWL and SKOS as Turtle (text) or JSON-LD (parsed)."""
        return self._request("GET", f"/v1.0/ontology-versions/{version_id}/export", params=self._clean({"format": fmt, "baseIri": base_iri}))

    def import_taxonomy(self, version_id: str, document: str, fmt: str = "turtle", mode: str = "merge") -> Any:
        """POST /v1.0/ontology-versions/{id}/taxonomy/import: import a SKOS document into a draft (merge or replace)."""
        content_type = "application/ld+json" if fmt == "jsonld" else "text/turtle"
        response = self._session.request(
            "POST",
            f"{self.base_url}/v1.0/ontology-versions/{version_id}/taxonomy/import",
            params={"format": fmt, "mode": mode},
            data=document.encode("utf-8"),
            headers=self._headers({"Content-Type": content_type}),
        )
        if not (200 <= response.status_code < 300):
            raise PneumaError(response.status_code, _safe_body(response))
        return response.json()

    def get_subject_ontology(self, subject_id: str) -> Any:
        """GET /v1.0/subjects/{id}/ontology: pinned version, effective definition, and classification settings."""
        return self._request("GET", f"/v1.0/subjects/{subject_id}/ontology")

    def set_subject_ontology(self, subject_id: str, ontology_version_id: Optional[str], retag: bool = True) -> Any:
        """PUT /v1.0/subjects/{id}/ontology: pin an approved version, or unpin with None."""
        return self._request("PUT", f"/v1.0/subjects/{subject_id}/ontology", json_body={"ontologyVersionId": ontology_version_id, "retag": retag})

    def list_ontology_violations(self, subject_id: str, status: Optional[str] = None, job_id: Optional[str] = None, max_results: Optional[int] = None, skip: Optional[int] = None) -> Any:
        """GET /v1.0/subjects/{id}/ontology-violations, optionally by status (Recorded, Quarantined, Released, Dismissed)."""
        return self._request("GET", f"/v1.0/subjects/{subject_id}/ontology-violations", params=self._clean({"status": status, "jobId": job_id, "maxResults": max_results, "skip": skip}))

    def release_ontology_violation(self, violation_id: str) -> Any:
        """POST /v1.0/ontology-violations/{id}/release: release a quarantined element into the graph."""
        return self._request("POST", f"/v1.0/ontology-violations/{violation_id}/release")

    def dismiss_ontology_violation(self, violation_id: str) -> Any:
        """POST /v1.0/ontology-violations/{id}/dismiss."""
        return self._request("POST", f"/v1.0/ontology-violations/{violation_id}/dismiss")

    def list_ontology_operations(self, subject_id: str, max_results: Optional[int] = None, skip: Optional[int] = None) -> Any:
        """GET /v1.0/subjects/{id}/ontology-operations."""
        return self._request("GET", f"/v1.0/subjects/{subject_id}/ontology-operations", params=self._clean({"maxResults": max_results, "skip": skip}))

    def start_ontology_operation(self, subject_id: str, kind: str, sample_size: Optional[int] = None) -> Any:
        """POST /v1.0/subjects/{id}/ontology-operations: queue Validate, Retag, or DriftCheck."""
        body: Dict[str, Any] = {"kind": kind}
        if sample_size is not None:
            body["sampleSize"] = sample_size
        return self._request("POST", f"/v1.0/subjects/{subject_id}/ontology-operations", json_body=body)

    def get_ontology_operation(self, operation_id: str) -> Any:
        """GET /v1.0/ontology-operations/{id}: the operation and its items."""
        return self._request("GET", f"/v1.0/ontology-operations/{operation_id}")

    def export_subject_graph(self, subject_id: str, fmt: str = "json", base_iri: Optional[str] = None) -> Any:
        """GET /v1.0/subjects/{id}/graph/export: json or jsonld (parsed), turtle or graphml (text)."""
        return self._request("GET", f"/v1.0/subjects/{subject_id}/graph/export", params=self._clean({"format": fmt, "baseIri": base_iri}))

    def clear_classification_cache(self, subject_id: str) -> Any:
        """DELETE /v1.0/subjects/{id}/classification-cache: remove the cache entries the subject stored."""
        return self._request("DELETE", f"/v1.0/subjects/{subject_id}/classification-cache")

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
        failure_category: Optional[str] = None,
        has_warnings: Optional[bool] = None,
    ) -> Any:
        """GET /v1.0/jobs (optional status filter) -> EnumerationResult envelope.

        Args:
            status: Optional job status filter.
            failure_category: Optional failure-category filter (for example ``"Fetch"``).
            has_warnings: Optional filter: True for jobs with warnings, False for jobs without.
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
                max_results,
                skip,
                order,
                search,
                extra={
                    "status": status,
                    "failureCategory": failure_category,
                    "hasWarnings": None if has_warnings is None else ("true" if has_warnings else "false"),
                },
            ),
        )

    def get_job(self, job_id: str) -> Any:
        """GET /v1.0/jobs/{id} -> { job, events, attempts, remediation }."""
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
        """POST /v1.0/model-runners.

        ``runner`` accepts ``name``, ``provider`` (one of ``OpenAI``,
        ``OpenAICompatible``, ``Gemini``, ``Ollama``, ``AzureOpenAI``,
        ``Anthropic``, ``Bedrock``, ``VoyageAI``, ``VertexAI``), ``endpoint``,
        ``apiFormat``, ``deployment``, ``apiVersion``, ``region``, ``project``,
        ``accessKeyId`` and ``active``. Secret material (``apiKey``,
        ``secretAccessKey``, ``sessionToken``) is write-only: it may be sent here
        but is never returned on reads.
        """
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
    # Subject prompts
    # ------------------------------------------------------------------

    def list_subject_prompts(self, subject_id: str) -> Any:
        """GET /v1.0/subjects/{subjectId}/prompts -> list of effective prompts.

        Args:
            subject_id: The subject whose prompts to list.

        Returns:
            A list of prompt entries, each with ``key``, ``name``,
            ``effectiveContent``, ``globalContent``, ``overrideContent`` (may be
            ``None``), ``source`` (``"Global"`` or ``"SubjectOverride"``), and
            ``mergeMode`` (``"Append"`` or ``"Replace"``).
        """
        return self._request("GET", f"/v1.0/subjects/{subject_id}/prompts")

    def set_subject_prompt(
        self, subject_id: str, key: str, content: str, merge_mode: str = "Append"
    ) -> Any:
        """PUT /v1.0/subjects/{subjectId}/prompts/{key}. Sets a prompt override.

        Args:
            subject_id: The subject to override the prompt for.
            key: The prompt key to override.
            content: The override content to store.
            merge_mode: How the override combines with the global prompt, either
                ``"Append"`` (default) or ``"Replace"`` (sent as ``mergeMode``).

        Returns:
            The subject's effective prompt for the key after the update.
        """
        body: Dict[str, Any] = {"content": content, "mergeMode": merge_mode}
        return self._request(
            "PUT", f"/v1.0/subjects/{subject_id}/prompts/{key}", json_body=body
        )

    def delete_subject_prompt(self, subject_id: str, key: str) -> None:
        """DELETE /v1.0/subjects/{subjectId}/prompts/{key}.

        Removes the subject-level override for ``key``, reverting it to the
        global prompt.
        """
        return self._request("DELETE", f"/v1.0/subjects/{subject_id}/prompts/{key}")

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

    def query(
        self,
        question: str,
        max_results: int = 8,
        subject_id: Optional[str] = None,
        metadata_filter: Optional[Dict[str, Any]] = None,
    ) -> Any:
        """POST /v1.0/query -> grounded answer with sources.

        Args:
            question: The natural-language question.
            max_results: Maximum sources to retrieve.
            subject_id: Optional subject to scope retrieval to (sent as
                ``subjectId``).
            metadata_filter: Optional facet filter (sent as ``metadataFilter``)
                of the shape ``{"requiredLabels": [...], "excludedLabels": [...],
                "requiredTags": [{"key", "condition", "value"}], "excludedTags":
                [...]}``. Merged with the subject's default filter to scope
                retrieval to documents ingested with matching labels/tags.
        """
        body: Dict[str, Any] = {"question": question, "maxResults": max_results}
        if subject_id is not None:
            body["subjectId"] = subject_id
        if metadata_filter is not None:
            body["metadataFilter"] = metadata_filter
        return self._request("POST", "/v1.0/query", json_body=body)

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
