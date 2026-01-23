.env
ADFS_API=https://doof-auth-adfs.d8200.mil
APP_AUTHENTICATION_ROUTE=https://doof.d8200.mil/api/login/

routes/login.py
import urllib.parse

from fastapi import APIRouter, Depends, Response, status

from consts import TOKEN_COOKIE_EXPIRATION, TOKEN_COOKIE_NAME
from loggers import loggers
from server.clients.adfs import ADFSClient
from server.deps.auth import get_full_name, get_username
from server.types import APIStatus, VerifyResponse

login_router = APIRouter(prefix="/api/login")


@login_router.get("", tags=["Auth"], name="get user info")
async def get_login_info(
    username: str = Depends(get_username),
    full_name: str = Depends(get_full_name),
):
    return VerifyResponse(
        status=APIStatus.ok,
        username=username,
        full_name=full_name,
    )


@login_router.get(
    "/{redirect_url}/",
    response_model=None,
    tags=["Auth"],
    name="set adfs token",
)
async def adfs_redirect_with_token(
    redirect_url: str,
    token: str,
    username: str = Depends(get_username),
):
    is_token_valid = ADFSClient.validate_token(token)
    if not is_token_valid:
        return Response(status_code=status.HTTP_401_UNAUTHORIZED)

    redirect_url = urllib.parse.unquote_plus(redirect_url) or "/"

    response = Response(
        status_code=status.HTTP_302_FOUND,
        headers={"Location": redirect_url},
    )

    response.set_cookie(
        TOKEN_COOKIE_NAME,
        value=token,
        expires=TOKEN_COOKIE_EXPIRATION,
        path="/",
        secure=True,
        httponly=True,
        samesite="lax",
    )

    loggers.doof.info(
        {
            "function": "login",
            "message": "user login successfully",
            "username": username,
            "context": {},
        }
    )

    return response

adfs_claims.py
from typing import TypedDict


class ADFSClaims(TypedDict):
    SAMAccountName: str  # username (without @example.com)
    DisplayName: str     # classic display name
    GivenName: str       # first name
    Surname: str         # last name
    UPN: str             # email
    auth_time: str

adfs_client.py
import os
import urllib.parse

import requests
from fastapi import Request, Response, status

from data_types import ADFSClaims
from server.errors import AuthException

ADFS_API = os.environ["ADFS_API"]
APP_AUTHENTICATION_ROUTE = os.environ["APP_AUTHENTICATION_ROUTE"]

ADFS_REDIRECTED_URL = f"{ADFS_API}/authentication?tokenConsumerURL={{url}}"
ADFS_VALIDATE_TOKEN_ENDPOINT = f"{ADFS_API}/authorization/validate"
ADFS_GET_CLAIMS_ENDPOINT = f"{ADFS_API}/authorization/getClaims"


class ADFSClient:
    @staticmethod
    def redirect_request_to_adfs_auth(request: Request) -> Response:
        full_endpoint = request.url.path
        if request.url.query:
            full_endpoint += f"?{request.url.query}"

        # three url encodes: first for the adfs, second for fastapi, third for python
        escaped_endpoint = urllib.parse.quote_plus(
            urllib.parse.quote_plus(
                urllib.parse.quote_plus(full_endpoint)
            )
        )

        token_consumer_url = f"{APP_AUTHENTICATION_ROUTE}{escaped_endpoint}"
        headers = {
            "Location": ADFS_REDIRECTED_URL.format(
                url=token_consumer_url
            )
        }

        return Response(
            status_code=status.HTTP_302_FOUND,
            headers=headers,
        )

    @staticmethod
    def validate_token(token: str) -> bool:
        response = requests.get(
            ADFS_VALIDATE_TOKEN_ENDPOINT,
            params={"token": token},
        )
        return response.text == "true"

    @staticmethod
    def get_claims(token: str) -> ADFSClaims:
        response = requests.get(
            ADFS_GET_CLAIMS_ENDPOINT,
            params={"token": token},
        )

        if response.status_code != status.HTTP_200_OK:
            raise AuthException("Received unauthorized from adfs")

        return response.json()

auth_auth.py
from fastapi import Request, status
from fastapi.responses import Response
from starlette.middleware.base import BaseHTTPMiddleware, RequestResponseEndpoint

from consts import TOKEN_COOKIE_NAME, TOKEN_QUERY_PARAM
from server.clients import ADFSClient
from server.middlewares.adfs.request_whitelist import is_request_in_whitelist


class ADFSAuthMiddleware(BaseHTTPMiddleware):
    async def dispatch(
        self,
        request: Request,
        call_next: RequestResponseEndpoint,
    ) -> Response:
        if is_request_in_whitelist(request):
            return await call_next(request)

        token = self._get_token(request)

        if not token:
            return self._handle_unauthenticated_request(request)

        request.scope["state"]["token"] = token
        response = await call_next(request)
        return response

    def _get_token(self, request: Request) -> str | None:
        cookie_token = request.cookies.get(TOKEN_COOKIE_NAME)
        query_token = request.query_params.get(
            TOKEN_QUERY_PARAM,
            cookie_token,
        )
        return query_token

    def _handle_unauthenticated_request(
        self,
        request: Request,
    ) -> Response:
        if request.url.path.startswith("/api"):
            return Response(
                status_code=status.HTTP_401_UNAUTHORIZED
            )

        return ADFSClient.redirect_request_to_adfs_auth(request)

load_user_info.py
import os

from cachetools import TTLCache
from fastapi import Request, Response, status
from starlette.middleware.base import BaseHTTPMiddleware, RequestResponseEndpoint

from data_types.adfs_claims import ADFSClaims
from data_types.user_info import UserInfo
from server.clients import ADFSClient
from server.errors import AuthException
from consts import TOKEN_COOKIE_NAME
from server.middlewares.adfs.request_whitelist import is_request_in_whitelist

users_info_cache: TTLCache[str, UserInfo] = TTLCache(
    maxsize=2000,
    ttl=3600 * 8,
)


class LoadUserInfoMiddleware(BaseHTTPMiddleware):
    async def dispatch(
        self,
        request: Request,
        call_next: RequestResponseEndpoint,
    ) -> Response:
        if is_request_in_whitelist(request):
            return await call_next(request)

        token: str = request.scope["state"]["token"]
        user_info = users_info_cache.get(token)

        if not user_info:
            try:
                user_info = self._get_user_claims(token)
                users_info_cache[token] = user_info
            except AuthException:
                response = Response(
                    status_code=status.HTTP_401_UNAUTHORIZED
                )
                response.delete_cookie(TOKEN_COOKIE_NAME)
                return response

        request.scope["state"]["username"] = user_info.username
        request.scope["state"]["full_name"] = user_info.full_name

        return await call_next(request)

    def _get_user_claims(self, token: str) -> UserInfo:
        claims = ADFSClient.get_claims(token)
        return self._format_claims(claims)

    def _format_claims(self, claims: ADFSClaims) -> UserInfo:
        given_name = claims["GivenName"]
        surname = claims["Surname"]
        username = claims["UPN"]
        full_name = f"{given_name} {surname}"

        return UserInfo(
            username=username,
            full_name=full_name,
        )