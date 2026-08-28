"""Balance endpoint providers shared by the Qt pet and Python fallback."""

from __future__ import annotations

import json
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Mapping, Protocol


class ProviderError(RuntimeError):
    """A concise error suitable for the pet status bubble."""


@dataclass(frozen=True)
class BalanceSnapshot:
    amount: float
    currency: str
    fetched_at: datetime


class BalanceSettings(Protocol):
    endpoint: str
    auth_mode: str
    token_blob: str
    balance_path: str
    currency: str


def read_json_path(payload: object, path: str) -> object:
    current = payload
    for part in filter(None, (segment.strip() for segment in path.split('.'))):
        if isinstance(current, Mapping):
            if part not in current:
                raise ProviderError(f"JSON path not found: {path}")
            current = current[part]
        elif isinstance(current, list) and part.isdigit():
            try:
                current = current[int(part)]
            except IndexError as exc:
                raise ProviderError(f"JSON path not found: {path}") from exc
        else:
            raise ProviderError(f"JSON path not found: {path}")
    return current


def parse_amount(value: object) -> float:
    if isinstance(value, bool):
        raise ProviderError("balance is not a number")
    if isinstance(value, (int, float)):
        return float(value)
    cleaned = str(value).strip().replace(',', '')
    for symbol in ('$','USD','CNY','RMB','¥'):
        cleaned = cleaned.replace(symbol, '')
    try:
        return float(cleaned.strip())
    except ValueError as exc:
        raise ProviderError("balance is not a number") from exc


class GenericJsonProvider:
    def __init__(self, token_reader) -> None:
        self._token_reader = token_reader

    def fetch(self, settings: BalanceSettings) -> BalanceSnapshot:
        endpoint = settings.endpoint.strip()
        if not endpoint:
            raise ProviderError('请先配置余额 API 地址')
        try:
            parsed = urllib.parse.urlsplit(endpoint)
        except ValueError as exc:
            raise ProviderError('接口地址无效') from exc
        if parsed.scheme not in ('http', 'https') or not parsed.netloc:
            raise ProviderError('接口地址必须以 http:// 或 https:// 开头')

        headers = {'Accept': 'application/json', 'User-Agent': 'BalancePet/0.3'}
        try:
            token = self._token_reader(settings.token_blob)
        except Exception as exc:
            raise ProviderError('无法读取本机加密令牌') from exc
        token = token.strip()
        if settings.auth_mode in ('bearer', 'websee-session') and token.lower().startswith('bearer '):
            token = token[7:].strip()
        if token:
            if settings.auth_mode == 'websee-session':
                headers.update({
                    'Authorization': f'Bearer {token}',
                    'X-User-UI-Request': '1',
                    'Referer': f'{parsed.scheme}://{parsed.netloc}/dashboard',
                    'Accept-Language': 'zh',
                })
            elif settings.auth_mode == 'x-api-key':
                headers['x-api-key'] = token
            elif settings.auth_mode == 'authorization':
                headers['Authorization'] = token if ' ' in token else f'Bearer {token}'
            else:
                headers['Authorization'] = f'Bearer {token}'

        request = urllib.request.Request(endpoint, headers=headers, method='GET')
        try:
            with urllib.request.urlopen(request, timeout=15) as response:
                content = response.read().decode('utf-8')
        except urllib.error.HTTPError as exc:
            messages = {
                401: '认证失败（HTTP 401），请检查令牌和认证方式',
                403: '接口拒绝访问（HTTP 403）',
                404: '余额接口不存在（HTTP 404）',
            }
            raise ProviderError(messages.get(exc.code, f'余额接口返回 HTTP {exc.code}')) from exc
        except (urllib.error.URLError, TimeoutError, OSError) as exc:
            raise ProviderError(f'无法连接余额接口：{exc}') from exc
        try:
            payload = json.loads(content)
        except json.JSONDecodeError as exc:
            raise ProviderError('接口返回的内容不是 JSON') from exc
        selected = read_json_path(payload, settings.balance_path) if settings.balance_path.strip() else payload
        return BalanceSnapshot(
            parse_amount(selected),
            (settings.currency.strip() or 'USD').upper(),
            datetime.now(timezone.utc),
        )
