"""Pneuma Python SDK.

A lightweight client for the Pneuma REST API. See ``PneumaClient`` for the full
set of methods mirroring the API surface documented in REST_API.md.
"""

from .client import PneumaClient, PneumaError

__all__ = ["PneumaClient", "PneumaError"]

__version__ = "0.1.0"
